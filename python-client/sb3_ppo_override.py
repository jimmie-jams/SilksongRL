import json
import os
import re
from dataclasses import dataclass, field
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


@dataclass
class _Step:
    obs: Any
    action: np.ndarray
    reward: float
    episode_start: bool
    value: float
    log_prob: float


@dataclass
class _Game:
    """One connected game's steps since its last update, and where its current episode stands."""
    steps: List[_Step] = field(default_factory=list)
    last_done: bool = False
    next_obs: Any = None
    episode_reward: float = 0.0


def _stack(observations: List[Any]) -> Any:
    """Batch per-game observations, flat arrays or dicts of arrays."""
    if isinstance(observations[0], dict):
        return {key: np.stack([o[key] for o in observations]) for key in observations[0]}
    return np.stack(observations)



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

        self.boss_name = boss_name
        self.save_freq = save_freq

        self.times_trained = 0
        self.episodes_completed = 0
        self.episode_rewards: List[float] = []

        # Keyed by the server's connection id. Never saved: unfinished steps die with the server.
        self.games: Dict[int, _Game] = {}

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
        return super()._excluded_save_params() + list(self.STATS) + ["games"]

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
        game_id: int,
        obs: Any,  # Can be List[float] or Dict[str, np.ndarray]
        action: List[int],
        reward: float,
        next_obs: Any,
        done: bool
    ) -> None:
        """Store one of a game's transitions, and train once there are enough (_train_while_ready)."""
        # Convert to numpy if flat list
        if isinstance(obs, list):
            obs = np.array(obs, dtype=np.float32)
        if isinstance(next_obs, list):
            next_obs = np.array(next_obs, dtype=np.float32)
        action = np.array(action, dtype=np.int32)

        game = self.games.setdefault(game_id, _Game())
        game.episode_reward += reward
        if done:
            self.episodes_completed += 1
            self.episode_rewards.append(game.episode_reward)
            game.episode_reward = 0.0

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

        game.steps.append(_Step(obs, action, reward, game.last_done, value.item(), log_prob.item()))
        game.last_done = done
        game.next_obs = next_obs

        self._train_while_ready()


    def remove_game(self, game_id: int) -> None:
        """A game disconnected. Drop its unfinished steps so it does not hold up the others."""
        self.games.pop(game_id, None)
        self._train_while_ready()


    def _train_while_ready(self) -> None:
        # Train once every game has a full share. Games that get there first keep playing, and
        # their extra steps go into the next update. Once a game has two shares queued, train
        # without the games still short: one that stopped sending steps (P, a failed reset) or
        # runs slower would otherwise hold up training while the others' steps pile up.
        n = self.n_steps
        while True:
            full = [g for g in self.games.values() if len(g.steps) >= n]
            if not full or (len(full) < len(self.games) and max(len(g.steps) for g in full) < 2 * n):
                return
            self._train_on_games(full)


    def _train_on_games(self, games: List[_Game]) -> None:
        """Train on n_steps from each of these games, one buffer column per game."""
        n = self.n_steps

        # Bootstrap from what follows each game's share: its next stored step if it has one,
        # otherwise the observation its latest transition ended on
        last_values, dones = [], []
        for game in games:
            if len(game.steps) > n:
                last_values.append(game.steps[n].value)
                dones.append(game.steps[n].episode_start)
            else:
                with torch.no_grad():
                    last_values.append(self.policy.predict_values(self._obs_to_tensor(game.next_obs)).item())
                dones.append(game.last_done)

        buffer = self.rollout_buffer_class(
            n,
            self.observation_space,
            self.action_space,
            device=self.device,
            gamma=self.gamma,
            gae_lambda=self.gae_lambda,
            n_envs=len(games),
            **self.rollout_buffer_kwargs,
        )
        for t in range(n):
            row = [game.steps[t] for game in games]
            buffer.add(
                obs=_stack([step.obs for step in row]),
                action=np.array([step.action for step in row]),
                reward=np.array([step.reward for step in row], dtype=np.float32),
                episode_start=np.array([step.episode_start for step in row], dtype=np.float32),
                value=torch.tensor([step.value for step in row]),
                log_prob=torch.tensor([step.log_prob for step in row]),
            )
        buffer.compute_returns_and_advantage(
            last_values=torch.tensor(last_values), dones=np.array(dones, dtype=np.float32)
        )
        for game in games:
            del game.steps[:n]

        print(f"[CustomPPO] Training on {len(games)} game(s) x {n} steps")
        self.rollout_buffer = buffer
        self.train()
        self.times_trained += 1
