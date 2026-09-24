import json
import os
import re
import numpy as np
import torch
from stable_baselines3 import PPO
import matplotlib.pyplot as plt
from typing import Dict, List, Any, Optional


# Checkpoints at multiples of this many attempts are kept, the rest get replaced by the next save
KEEP_EVERY = 1000


def _boss_directory(boss_name: str) -> str:
    return os.path.join("models", boss_name)


def _checkpoints(boss_name: str) -> Dict[int, str]:
    """Checkpoints in the boss's folder, keyed by the attempt count in their name (Lace_1_350.zip)."""
    boss_dir = _boss_directory(boss_name)
    if not os.path.isdir(boss_dir):
        return {}

    pattern = re.compile(rf"{re.escape(boss_name)}_(\d+)\.zip")
    found = {}
    for file_name in os.listdir(boss_dir):
        match = pattern.fullmatch(file_name)
        if match:
            found[int(match.group(1))] = os.path.join(boss_dir, file_name)
    return found


def latest_checkpoint(boss_name: str) -> Optional[str]:
    checkpoints = _checkpoints(boss_name)
    return checkpoints[max(checkpoints)] if checkpoints else None


def _stats_path(checkpoint_path: str) -> str:
    return os.path.splitext(checkpoint_path)[0] + ".json"



class CustomPPO(PPO):
    # Training stats, saved next to each checkpoint (Lace_1_350.json) rather than inside it
    STATS = ("episodes_completed", "times_trained", "episode_rewards")

    def __init__(
        self,
        *args: Any,
        boss_name: Optional[str] = None,
        save_freq: int = 50,
        **kwargs: Any
    ) -> None:
        super().__init__(*args, **kwargs)

        self.last_done = False
        self.boss_name = boss_name
        self.save_freq = save_freq

        self.times_trained = 0
        self.episodes_completed = 0
        self.episode_rewards: List[float] = []
        self.current_episode_reward = 0.0

        # Initilalize logger or SB3 complains
        if not hasattr(self, '_logger') or self._logger is None:
            from stable_baselines3.common.logger import configure
            self._logger = configure()

        # Only reset buffer if it exists (it won't exist during .load())
        if hasattr(self, 'rollout_buffer') and self.rollout_buffer is not None:
            self.rollout_buffer.reset()
    

    @property
    def logger(self):
        return self._logger

    def _excluded_save_params(self) -> List[str]:
        return super()._excluded_save_params() + list(self.STATS)

    @classmethod
    def load(cls, path: str, **kwargs: Any) -> "CustomPPO":
        # kwargs (boss_name too) end up as attributes, overriding what the checkpoint stored
        model = super().load(path, **kwargs)

        # Older checkpoints have no .json, their stats are inside the zip and already restored
        stats_path = _stats_path(path)
        if os.path.exists(stats_path):
            with open(stats_path) as f:
                model.__dict__.update(json.load(f))
        elif model.episodes_completed == 0:
            print(f"[CustomPPO] No {os.path.basename(stats_path)} next to the checkpoint, stats start from 0")

        return model


    def start_new_rollout(self) -> None:
        self.rollout_buffer.reset()


    def save_checkpoint(self) -> str:
        """
        Save as models/<boss>/<boss>_<attempts>.zip plus its stats .json,
        replacing the previous checkpoint unless it's a keeper.
        """
        previous = _checkpoints(self.boss_name)

        boss_dir = _boss_directory(self.boss_name)
        os.makedirs(boss_dir, exist_ok=True)
        path = os.path.join(boss_dir, f"{self.boss_name}_{self.episodes_completed}.zip")
        self.save(path)
        with open(_stats_path(path), "w") as f:
            json.dump({name: getattr(self, name) for name in self.STATS}, f)
        # self.plot_rewards(boss_dir)

        for attempts, old_path in previous.items():
            keeper = attempts > 0 and attempts % KEEP_EVERY == 0
            if old_path != path and not keeper:
                os.remove(old_path)
                if os.path.exists(_stats_path(old_path)):
                    os.remove(_stats_path(old_path))
        return path


    def plot_rewards(self, save_dir: str) -> None:
        """Generate and save a plot of episode rewards."""

        if len(self.episode_rewards) == 0:
            raise ValueError(f"No rewards to plot for {self.boss_name}")
        
        plt.figure(figsize=(12, 6))

        rewards = np.asarray(self.episode_rewards, dtype=np.float32)
        episodes = list(range(1, len(self.episode_rewards) + 1))
        
        plt.plot(episodes, rewards, alpha=0.3, label='Episode Rewards', color='blue')
        
        window = self.save_freq // 2
        moving_avg = np.convolve(rewards, np.ones(window)/window, mode='valid')
        ma_start = window
        ma_episodes = list(range(ma_start, ma_start + len(moving_avg)))
        plt.plot(ma_episodes, moving_avg, label=f'{window}-Episode Moving Average Rewards', 
                color='red', linewidth=2)
        
        plt.xlabel('Episode')
        plt.ylabel('Total Reward')
        plt.title(f'Training Progress - {self.boss_name}')
        plt.legend()
        plt.grid(True, alpha=0.3)
        
        plot_path = os.path.join(save_dir, 'training_rewards.png')
        plt.savefig(plot_path, dpi=150, bbox_inches='tight')
        plt.close()


    def _obs_to_tensor(self, obs: Any) -> torch.Tensor:
        """Convert observation (flat array or dict) to tensor for policy."""
        if isinstance(obs, dict):
            # Dict observation - convert each part
            return {k: torch.as_tensor(v).float().unsqueeze(0).to(self.device) for k, v in obs.items()}
        else:
            # Flat array observation
            if not isinstance(obs, np.ndarray):
                obs = np.array(obs, dtype=np.float32)
            return torch.as_tensor(obs).float().unsqueeze(0).to(self.device)

    def store_transition(
        self,
        obs: Any,  # Can be List[float] or Dict[str, np.ndarray]
        action: List[int],
        reward: float,
        next_obs: Any,
        done: bool
    ) -> None:
        """Store a transition in the rollout buffer."""
        # Convert to numpy if flat list
        if isinstance(obs, list):
            obs = np.array(obs, dtype=np.float32)
        if isinstance(next_obs, list):
            next_obs = np.array(next_obs, dtype=np.float32)
        action = np.array(action, dtype=np.int32)
        
        self.current_episode_reward += reward
        if done:

            self.episodes_completed += 1
            self.episode_rewards.append(self.current_episode_reward)
            self.current_episode_reward = 0.0
            
            if self.save_freq and self.episodes_completed % self.save_freq == 0:
                path = self.save_checkpoint()
                print(f"Checkpoint saved after {self.episodes_completed} episodes: {path}")
        
        obs_t = self._obs_to_tensor(obs)
        action_t = torch.as_tensor(action).unsqueeze(0).to(self.device)

        with torch.no_grad():
            dist = self.policy.get_distribution(obs_t)
            value = self.policy.predict_values(obs_t)
            log_prob = dist.log_prob(action_t)
            if log_prob.dim() > 1:
                log_prob = log_prob.sum(-1)

        self.rollout_buffer.add(
            obs=obs,
            action=action,
            reward=reward,
            episode_start=self.last_done,
            value=value.squeeze(),
            log_prob=log_prob.squeeze(),
        )
        
        self.last_done = done
        
        if self.rollout_buffer.pos >= self.n_steps:
            self.finish_rollout_and_train(next_obs)


    def finish_rollout_and_train(self, next_obs: Any) -> None:
        """Finish a rollout and train the model."""
        with torch.no_grad():
            next_obs_t = self._obs_to_tensor(next_obs)
            last_values = self.policy.predict_values(next_obs_t)

        self.rollout_buffer.compute_returns_and_advantage(
            last_values=last_values, dones=np.array([False])
        )

        self.train()
        self.rollout_buffer.reset()
        self.times_trained += 1


