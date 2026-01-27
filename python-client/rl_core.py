import os
from typing import Optional, Dict, Any, List, Tuple

import gymnasium as gym
import numpy as np
from gymnasium import spaces

from sb3_ppo_override import CustomPPO

model: Optional[CustomPPO] = None
obs_dim: Optional[int] = None
action_shape: Optional[List[int]] = None
current_boss: Optional[str] = None
obs_type: Optional[str] = None          # 'vector' or 'hybrid'
vector_obs_dim: Optional[int] = None    # Size of vector portion
visual_width: Optional[int] = None      # Width of visual observation (0 if vector-only)
visual_height: Optional[int] = None     # Height of visual observation (0 if vector-only)


class DummyEnv(gym.Env):
    """
    Minimal env to satisfy SB3.
    Supports both vector-only and hybrid (vector + visual) observation modes.
    """

    def __init__(
        self, 
        obs_size: int, 
        action_space_shape: List[int] = None,
        observation_type: str = "vector",
        vector_obs_size: int = None,
        visual_w: int = 0,
        visual_h: int = 0
    ) -> None:
        super().__init__()
        self.obs_size = obs_size
        self.observation_type = observation_type
        self.vector_obs_size = vector_obs_size if vector_obs_size else obs_size
        self.visual_width = visual_w
        self.visual_height = visual_h
        
        if observation_type == "hybrid" and visual_w > 0 and visual_h > 0:
            # Dict observation space for hybrid: separate vector and visual
            self.observation_space = spaces.Dict({
                "vector": spaces.Box(0.0, 1.0, shape=(self.vector_obs_size,), dtype=np.float32),
                "visual": spaces.Box(0.0, 1.0, shape=(1, visual_h, visual_w), dtype=np.float32),  # (C, H, W)
            })
        else:
            # Flat observation space for vector-only
            self.observation_space = spaces.Box(0.0, 1.0, shape=(obs_size,), dtype=np.float32)
        
        self.action_space = spaces.MultiDiscrete(action_space_shape)

    def _make_obs(self) -> Any:
        """Create a zero observation matching the observation space."""
        if self.observation_type == "hybrid" and self.visual_width > 0:
            return {
                "vector": np.zeros(self.vector_obs_size, dtype=np.float32),
                "visual": np.zeros((1, self.visual_height, self.visual_width), dtype=np.float32),
            }
        else:
            return np.zeros(self.obs_size, dtype=np.float32)

    def reset(
        self,
        *,
        seed: Optional[int] = None,
        options: Optional[Dict[str, Any]] = None,
    ) -> Tuple[Any, Dict[str, Any]]:
        return self._make_obs(), {}

    def step(self, action: np.ndarray) -> Tuple[Any, float, bool, bool, Dict[str, Any]]:
        return self._make_obs(), 0.0, True, False, {}


def normalize_boss_name(boss_name: str) -> str:
    return boss_name.replace(" ", "_").lower()


def initialize_model(
    obs_size: int, 
    boss_name: str, 
    action_space_shape: List[int] = None,
    observation_type: str = "vector",
    vector_obs_size: int = None,
    visual_w: int = 0,
    visual_h: int = 0
) -> Dict[str, Any]:
    """Initialize or load model; returns metadata about the initialization."""
    global model, obs_dim, action_shape, current_boss, obs_type, vector_obs_dim
    global visual_width, visual_height

    obs_dim = obs_size
    current_boss = boss_name
    obs_type = observation_type
    vector_obs_dim = vector_obs_size
    visual_width = visual_w
    visual_height = visual_h
    action_shape = action_space_shape

    normalized_boss_name = normalize_boss_name(boss_name)
    checkpoint_path = f"models/{normalized_boss_name}/checkpoint.zip"

    print(f"[RLCore] Observation type: {obs_type}")
    print(f"[RLCore] Total obs size: {obs_dim}, Vector obs size: {vector_obs_dim}")
    if obs_type == "hybrid":
        print(f"[RLCore] Visual obs: {visual_width}x{visual_height} ({visual_width * visual_height})")
    print(f"[RLCore] Action space shape: {action_space_shape}")

    env = DummyEnv(
        obs_size, 
        action_space_shape,
        observation_type=observation_type,
        vector_obs_size=vector_obs_size,
        visual_w=visual_w,
        visual_h=visual_h
    )
    

    if observation_type == "hybrid" and visual_w > 0:
        policy = "MultiInputPolicy"
        policy_kwargs = dict(
            net_arch=[256, 256, 128],
        )
    else:
        policy = "MlpPolicy"
        policy_kwargs = dict(net_arch=[256, 256, 128])

    if os.path.exists(checkpoint_path):
        print(f"[RLCore] Loading checkpoint: {checkpoint_path}")
        model = CustomPPO.load(
            checkpoint_path,
            env=env,
            device="cpu",
        )
        checkpoint_loaded = True
    else:
        print(f"[RLCore] No checkpoint found, initializing fresh model")
        print(f"[RLCore] Using policy: {policy}")
        model = CustomPPO(
            policy,
            env,
            boss_name=normalized_boss_name,
            verbose=1,
            n_steps=2048,
            batch_size=512,
            learning_rate=3e-4,
            ent_coef=0.01,
            clip_range=0.2,
            n_epochs=10,
            gamma=0.99,
            gae_lambda=0.95,
            max_grad_norm=0.5,
            policy_kwargs=policy_kwargs,
        )
        checkpoint_loaded = False
        
        model_dir = os.path.dirname(checkpoint_path)
        os.makedirs(model_dir, exist_ok=True)
        model.save(checkpoint_path.replace(".zip", ""))
        print(f"[RLCore] Saved initial checkpoint: {checkpoint_path}")

    return {
        "initialized": True,
        "boss_name": boss_name,
        "observation_size": obs_dim,
        "checkpoint_loaded": checkpoint_loaded,
    }


def _convert_to_obs(state: List[float]) -> Any:
    """Convert flat state array to observation format (flat or dict)."""
    if obs_type == "hybrid" and visual_width > 0:
        state_arr = np.array(state, dtype=np.float32)
        return {
            "vector": state_arr[:vector_obs_dim],
            "visual": state_arr[vector_obs_dim:].reshape(1, visual_height, visual_width),  # (C, H, W)
        }
    else:
        return np.array(state, dtype=np.float32)


def get_action(state: List[float]) -> List[int]:
    if model is None:
        raise ValueError("Model not initialized")
    if len(state) != obs_dim:
        raise ValueError(f"Expected obs size {obs_dim}, got {len(state)}")

    obs = _convert_to_obs(state)
    action, _ = model.predict(obs, deterministic=False)
    return action.tolist()


def store_transition(state: List[float], action: List[int], reward: float, next_state: List[float], done: bool) -> None:
    if model is None:
        raise ValueError("Model not initialized")
    if len(state) != obs_dim or len(next_state) != obs_dim:
        raise ValueError("Observation size mismatch")
    if len(action) != len(action_shape):
        raise ValueError(f"Action size mismatch: expected {len(action_shape)}, got {len(action)}")

    obs = _convert_to_obs(state)
    next_obs = _convert_to_obs(next_state)
    model.store_transition(obs, action, reward, next_obs, done)


