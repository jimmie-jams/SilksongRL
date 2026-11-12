"""
Graph a boss's training as of its latest checkpoint, next to that checkpoint in models/<boss>/:
<boss>_<episodes>.png from its stats and <boss>_<episodes>_updates.png from updates.csv.

    python plot_training.py Lace_2
"""
import argparse
import csv
import json
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np

from mod_config import normalize_encounter
from sb3_ppo_override import latest_checkpoint


RAW_COLOR = "#4c78a8"
AVERAGE_COLOR = "#e45756"
CLIP_FRACTION_COLOR = "#f58518"

EPISODE_WINDOW = 500
UPDATE_WINDOW = 20

# Episodes are dense enough to need fainter raw lines than updates
EPISODE_RAW = {"alpha": 0.18, "linewidth": 0.6}
UPDATE_RAW = {"alpha": 0.35, "linewidth": 0.8}
KL_RAW = {"alpha": 0.28, "linewidth": 0.8}

plt.rcParams["font.size"] = 11


def _average(values, window):
    return np.convolve(values, np.ones(window) / window, mode="valid")


def _figure(title):
    fig, axes = plt.subplots(2, 2, figsize=(14, 9), sharex=True)
    fig.suptitle(title, fontsize=15, y=0.995)
    for ax in axes.flat:
        ax.grid(True, linewidth=0.6, alpha=0.35)
        ax.spines[["top", "right"]].set_visible(False)
    for ax in axes[1]:
        ax.set_xlabel("Episode")
    return fig, axes


def _save(fig, path):
    fig.tight_layout()
    fig.savefig(path, dpi=150, bbox_inches="tight")
    plt.close(fig)


def _panel(ax, x, y, unit, window, raw_style, title, ylabel, reference=None):
    """Every value faintly with their moving average on top, plus an optional (value, label) reference line."""
    ax.plot(x, y, color=RAW_COLOR, label=f"Each {unit}", **raw_style)
    ax.plot(x[window - 1:], _average(y, window), color=AVERAGE_COLOR, linewidth=2,
            label=f"{window}-{unit} moving average")
    if reference:
        ax.axhline(reference[0], color="#333333", linestyle="--", linewidth=1, label=reference[1])
    ax.set(title=title, ylabel=ylabel, xlim=(x[0], x[-1]))
    ax.legend(frameon=False)


def plot_episodes(stats, title, path):
    x = np.arange(1, stats["episodes_completed"] + 1)
    window = min(EPISODE_WINDOW, len(x))
    full_hp = max(stats["episode_boss_hp"])

    fig, axes = _figure(title)
    panels = [
        (axes[0, 0], "episode_rewards", "Episode reward", "Reward", None),
        (axes[0, 1], "episode_boss_hp", "Boss HP left at episode end", "Boss HP left",
         (full_hp, f"Full HP ({full_hp})")),
        (axes[1, 0], "episode_seconds", "Fight length", "Game seconds", None),
    ]
    for ax, stat, panel_title, ylabel, reference in panels:
        _panel(ax, x, np.asarray(stats[stat], dtype=float), "episode", window, EPISODE_RAW,
               panel_title, ylabel, reference)

    # A raw line would just be spikes to 100%. At least 1% tall, so a first kill or two doesn't fill the panel.
    ax = axes[1, 1]
    kills = np.asarray(stats["episode_boss_hp"]) <= 0
    rate = _average(kills * 100.0, window)
    ax.plot(x[window - 1:], rate, color=AVERAGE_COLOR, linewidth=2, label=f"{window}-episode moving average")
    ax.set(title=f"Kill rate (total kills: {kills.sum()})", ylabel="% of episodes", xlim=(x[0], x[-1]),
           ylim=(0, max(1, rate.max() * 1.1)))
    ax.legend(frameon=False)

    _save(fig, path)


def plot_updates(rows, title, path):
    def column(name):
        return np.array([float(row[name]) for row in rows])

    x = column("episodes")
    window = min(UPDATE_WINDOW, len(x))

    fig, axes = _figure(title)
    _panel(axes[0, 0], x, column("explained_variance"), "update", window, UPDATE_RAW, "Value explained variance",
           "Explained variance", (1.0, "Fully explained"))
    axes[0, 0].set_ylim(top=1.02)
    axes[0, 0].legend(loc="lower right", frameon=False)
    _panel(axes[0, 1], x, column("value_loss"), "update", window, UPDATE_RAW, "Value loss", "Value loss")
    _panel(axes[1, 0], x, -column("entropy_loss"), "update", window, UPDATE_RAW, "Policy entropy",
           "Entropy (−entropy_loss)")

    # Different scales, so the clip fraction gets its own axis
    ax = axes[1, 1]
    averaged_x = x[window - 1:]
    ax.plot(x, column("approx_kl"), color=RAW_COLOR, **KL_RAW)
    kl_line, = ax.plot(averaged_x, _average(column("approx_kl"), window), color=AVERAGE_COLOR, linewidth=2,
                       label=f"Approx KL, {window}-update average")
    ax.set(title="Policy step size", ylabel="Approx KL", xlim=(x[0], x[-1]))

    clip_ax = ax.twinx()
    clip_line, = clip_ax.plot(averaged_x, _average(column("clip_fraction"), window), color=CLIP_FRACTION_COLOR,
                              linewidth=2, label=f"Clip fraction, {window}-update average")
    clip_ax.set_ylabel("Clip fraction")
    clip_ax.spines[["top", "right"]].set_visible(False)
    ax.legend(handles=[kl_line, clip_line], frameon=False)

    _save(fig, path)


def _checkpoint_updates(updates_path, times_trained):
    """updates.csv rows that led to the checkpoint. Resuming from a checkpoint redoes the updates made
    since it, leaving their abandoned rows in the file, so the last row of each update number is the real one."""
    with open(updates_path, newline="") as f:
        rows = {int(row["update"]): row for row in csv.DictReader(f)}
    return [rows[update] for update in sorted(rows) if update <= times_trained]


def main():
    parser = argparse.ArgumentParser(description="Graph a boss's training as of its latest checkpoint.")
    parser.add_argument("boss", help="Boss name, e.g. Lace_2")
    boss = normalize_encounter(parser.parse_args().boss)

    checkpoint = latest_checkpoint(boss)
    if checkpoint is None:
        raise SystemExit(f"No checkpoints for {boss} in models/{boss}")
    checkpoint = Path(checkpoint)

    stats_path = checkpoint.with_suffix(".json")
    with open(stats_path) as f:
        stats = json.load(f)
    episodes = stats["episodes_completed"]
    if episodes == 0:
        raise SystemExit(f"{checkpoint.name} has no episodes to graph yet")

    name = boss.replace("_", " ")
    prefix = f"{boss.replace('_', '').lower()}_{episodes}"

    episodes_path = checkpoint.parent / f"{prefix}.png"
    plot_episodes(stats, f"{name} training  ·  {stats_path.name}  ·  {episodes:,} episodes", episodes_path)
    print(f"Saved {episodes_path}")

    if stats["times_trained"] == 0:
        print("No PPO updates yet, skipping the updates graph")
        return

    rows = _checkpoint_updates(checkpoint.parent / "updates.csv", stats["times_trained"])
    first, last = int(rows[0]["episodes"]), int(rows[-1]["episodes"])
    updates_path = checkpoint.parent / f"{prefix}_updates.png"
    plot_updates(rows, f"{name} PPO updates  ·  updates.csv  ·  {len(rows)} updates, "
                       f"episodes {first:,}–{last:,}", updates_path)
    print(f"Saved {updates_path}")


if __name__ == "__main__":
    main()
