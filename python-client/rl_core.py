from typing import Optional, Dict, Any, List, Tuple

import gymnasium as gym
import numpy as np
from gymnasium import spaces

from sb3_ppo_override import CustomPPO, latest_checkpoint

model: Optional[CustomPPO] = None
obs_dim: Optional[int] = None
action_shape: Optional[List[int]] = None
current_boss: Optional[str] = None
obs_type: Optional[str] = None          # 'vector', 'hybrid' or 'visual'
vector_obs_dim: Optional[int] = None    # Size of vector portion
visual_width: Optional[int] = None      # Width of visual observation (0 if vector-only)
visual_height: Optional[int] = None     # Height of visual observation (0 if vector-only)
visual_frames: Optional[int] = None     # Latest frames the policy sees (0 if vector-only)
checkpoint_loaded = False

# Per game, the frames its last action was chosen from, the latest last
frame_stacks: Dict[int, np.ndarray] = {}


class DummyEnv(gym.Env):
    """
    Minimal env to satisfy SB3.
    Supports vector-only, hybrid (vector + visual) and visual-only observations. The visual part
    is the latest few frames, stacked as channels.
    """

    def __init__(
        self, 
        obs_size: int, 
        action_space_shape: List[int] = None,
        observation_type: str = "vector",
        vector_obs_size: int = None,
        visual_w: int = 0,
        visual_h: int = 0,
        visual_f: int = 0
    ) -> None:
        super().__init__()
        self.obs_size = obs_size
        self.observation_type = observation_type
        self.vector_obs_size = vector_obs_size if vector_obs_size else obs_size
        self.visual_width = visual_w
        self.visual_height = visual_h

        if observation_type != "vector" and visual_w > 0 and visual_h > 0:
            # Dict observation space: the visual part, and for hybrid the vector part
            visual = spaces.Box(0.0, 1.0, shape=(visual_f, visual_h, visual_w), dtype=np.float32)  # (C, H, W)
            vector = spaces.Box(0.0, 1.0, shape=(self.vector_obs_size,), dtype=np.float32)
            self.observation_space = spaces.Dict(
                {"vector": vector, "visual": visual} if observation_type == "hybrid" else {"visual": visual})
        else:
            # Flat observation space for vector-only
            self.observation_space = spaces.Box(0.0, 1.0, shape=(obs_size,), dtype=np.float32)
        
        self.action_space = spaces.MultiDiscrete(action_space_shape)

    def _make_obs(self) -> Any:
        """Create a zero observation matching the observation space."""
        if isinstance(self.observation_space, spaces.Dict):
            return {key: np.zeros(space.shape, dtype=np.float32) for key, space in self.observation_space.items()}
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


def initialize_model(
    obs_size: int, 
    boss_name: str, 
    action_space_shape: List[int] = None,
    observation_type: str = "vector",
    vector_obs_size: int = None,
    visual_w: int = 0,
    visual_h: int = 0,
    visual_f: int = 0
) -> Dict[str, Any]:
    """
    Initialize or load model; returns metadata about the initialization. Only the first game to
    connect builds the model. The others join it, and must be playing the same thing.
    """
    global model, obs_dim, action_shape, current_boss, obs_type, vector_obs_dim
    global visual_width, visual_height, visual_frames, checkpoint_loaded

    if model is not None:
        joining = (boss_name, obs_size, list(action_space_shape or []), observation_type,
                   vector_obs_size, visual_w, visual_h, visual_f)
        running = (current_boss, obs_dim, list(action_shape or []), obs_type,
                   vector_obs_dim, visual_width, visual_height, visual_frames)
        if joining != running:
            raise ValueError(
                f"The server is already training {current_boss} (observation size {obs_dim}, "
                f"actions {action_shape}). A game on {boss_name} (observation size {obs_size}, "
                f"actions {action_space_shape}) cannot join it.")
        return {
            "initialized": True,
            "boss_name": boss_name,
            "observation_size": obs_dim,
            "checkpoint_loaded": checkpoint_loaded,
        }

    obs_dim = obs_size
    current_boss = boss_name
    obs_type = observation_type
    vector_obs_dim = vector_obs_size
    visual_width = visual_w
    visual_height = visual_h
    visual_frames = visual_f
    action_shape = action_space_shape

    checkpoint_path = latest_checkpoint(boss_name)

    print(f"[RLCore] Observation type: {obs_type}")
    print(f"[RLCore] Total obs size: {obs_dim}, Vector obs size: {vector_obs_dim}")
    if obs_type != "vector":
        print(f"[RLCore] Visual obs: {visual_frames} frame(s) of {visual_width}x{visual_height}"
              + (", vector only used for the reward" if obs_type == "visual" else ""))
    print(f"[RLCore] Action space shape: {action_space_shape}")

    env = DummyEnv(
        obs_size, 
        action_space_shape,
        observation_type=observation_type,
        vector_obs_size=vector_obs_size,
        visual_w=visual_w,
        visual_h=visual_h,
        visual_f=visual_f
    )
    

    if isinstance(env.observation_space, spaces.Dict):
        policy = "MultiInputPolicy"
        # The screen arrives as floats already in [0, 1]. SB3 only gives uint8 spaces a CNN unless
        # told the image is normalized, and would otherwise flatten it into the MLP.
        policy_kwargs = dict(
            net_arch=[256, 256, 128],
            normalize_images=False,
            features_extractor_kwargs=dict(normalized_image=True),
        )
        # The CNN trains several times faster on a GPU, if there is one
        device = "auto"
    else:
        policy = "MlpPolicy"
        policy_kwargs = dict(net_arch=[256, 256, 128])
        # SB3's advice for MLP-only policies: the CPU is quicker
        device = "cpu"

    if checkpoint_path:
        print(f"[RLCore] Loading checkpoint: {checkpoint_path}")
        model = CustomPPO.load(
            checkpoint_path,
            env=env,
            device=device,
            boss_name=boss_name,
        )
        checkpoint_loaded = True
        # A checkpoint keeps the architecture it was saved with
        if policy == "MultiInputPolicy" and not model.policy_kwargs.get("features_extractor_kwargs", {}).get("normalized_image"):
            print("[RLCore] WARNING: this checkpoint is from before the CNN and flattens the screen into the MLP. "
                  "Delete it to start a new model that sees the screen through a CNN.")
    else:
        print(f"[RLCore] No checkpoint found, initializing fresh model")
        print(f"[RLCore] Using policy: {policy}")
        model = CustomPPO(
            policy,
            env,
            boss_name=boss_name,
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
            device=device,
        )
        checkpoint_loaded = False

        initial_path = model.save_checkpoint()
        print(f"[RLCore] Saved initial checkpoint: {initial_path}")

    return {
        "initialized": True,
        "boss_name": boss_name,
        "observation_size": obs_dim,
        "checkpoint_loaded": checkpoint_loaded,
    }


def _convert_to_obs(state: List[float]) -> Any:
    """Convert flat state array to observation format (flat, or dict with the one frame sent).
    Anything after the frame is only there for the game's reward."""
    state_arr = np.array(state, dtype=np.float32)
    if obs_type == "vector" or visual_width == 0:
        return state_arr
    frame_end = vector_obs_dim + visual_height * visual_width
    obs = {"visual": state_arr[vector_obs_dim:frame_end].reshape(1, visual_height, visual_width)}  # (C, H, W)
    if obs_type == "hybrid":
        obs["vector"] = state_arr[:vector_obs_dim]
    return obs


def get_action(game_id: int, state: List[float], episode_start: bool = False) -> List[int]:
    if model is None:
        raise ValueError("Model not initialized")
    if len(state) != obs_dim:
        raise ValueError(f"Expected obs size {obs_dim}, got {len(state)}")

    obs = _convert_to_obs(state)
    if isinstance(obs, dict):
        # An episode starts with copies of its first frame
        stack = frame_stacks.get(game_id)
        if episode_start or stack is None:
            stack = np.repeat(obs["visual"], visual_frames, axis=0)
        else:
            stack = np.concatenate([stack[1:], obs["visual"]])
        frame_stacks[game_id] = obs["visual"] = stack
    action, _ = model.predict(obs, deterministic=False)
    return action.tolist()


def store_transition(game_id: int, state: List[float], action: List[int], reward: float, next_state: List[float], done: bool,
                     boss_hp: int, episode_seconds: float) -> None:
    if model is None:
        raise ValueError("Model not initialized")
    if len(state) != obs_dim or len(next_state) != obs_dim:
        raise ValueError("Observation size mismatch")
    if len(action) != len(action_shape):
        raise ValueError(f"Action size mismatch: expected {len(action_shape)}, got {len(action)}")

    obs = _convert_to_obs(state)
    next_obs = _convert_to_obs(next_state)
    if isinstance(obs, dict):
        # state is what the game's last action was chosen from, so get_action stacked it already.
        # None if the server restarted since.
        stack = frame_stacks.get(game_id)
        if stack is None:
            stack = np.repeat(obs["visual"], visual_frames, axis=0)
        obs["visual"] = stack
        next_obs["visual"] = np.concatenate([stack[1:], next_obs["visual"]])
    model.store_transition(game_id, obs, action, reward, next_obs, done, boss_hp, episode_seconds)


def remove_game(game_id: int) -> None:
    """A game disconnected: forget its unfinished steps."""
    frame_stacks.pop(game_id, None)
    if model is not None:
        model.remove_game(game_id)


