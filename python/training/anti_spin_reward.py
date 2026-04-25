"""Anti-spin reward shaping for KS0223 sim-to-real.

Observed pathology in v6/v7/v8 deploy: model emits constant max-steer command
which on real robot maps to in-place rotation → wall slam after a few cycles.
Even on sim eval, episodes that pass without rotation collapse on real frames.

This reward wrapper adds two shaping terms applied on top of base env reward:
1. **Penalty for action repetition:** if the same action is selected N (=5)
   times in a row without variety, accumulating penalty per repeated step.
   Discourages "stuck on one action" failure mode.
2. **Bonus for action variety after stagnation:** if action changes after a
   3+ repeat streak, bonus +0.2 once. Encourages exploration switching.

Use only during training. Disable for eval (set penalty/bonus to 0).
"""

from __future__ import annotations

from collections import deque
from typing import Any

import gymnasium as gym


class AntiSpinRewardWrapper(gym.Wrapper):
    """Adds anti-spin shaping to base reward."""

    def __init__(
        self,
        env: gym.Env,
        *,
        repeat_threshold: int = 5,
        repeat_penalty: float = 0.5,
        variety_bonus: float = 0.2,
        variety_after_streak: int = 3,
        history_len: int = 10,
    ):
        super().__init__(env)
        self.repeat_threshold = repeat_threshold
        self.repeat_penalty = repeat_penalty
        self.variety_bonus = variety_bonus
        self.variety_after_streak = variety_after_streak
        self.history: deque = deque(maxlen=history_len)
        self.current_streak = 0
        self.last_action: Any = None

    def reset(self, **kwargs):
        obs, info = self.env.reset(**kwargs)
        self.history.clear()
        self.current_streak = 0
        self.last_action = None
        return obs, info

    def step(self, action):
        obs, reward, terminated, truncated, info = self.env.step(action)

        # Track action streak (works for discrete int OR (throttle,steer) tuple)
        action_key = self._action_key(action)
        if action_key == self.last_action:
            self.current_streak += 1
        else:
            # Action changed
            if self.current_streak >= self.variety_after_streak:
                reward += self.variety_bonus
                info.setdefault("anti_spin", {})["variety_bonus"] = self.variety_bonus
            self.current_streak = 1

        if self.current_streak >= self.repeat_threshold:
            reward -= self.repeat_penalty
            info.setdefault("anti_spin", {})["repeat_penalty"] = self.repeat_penalty
            info["anti_spin"]["streak"] = self.current_streak

        self.history.append(action_key)
        self.last_action = action_key
        return obs, reward, terminated, truncated, info

    @staticmethod
    def _action_key(action):
        """Make a hashable key from action (handles int, tuple, or ndarray)."""
        try:
            # numpy scalar / int — direct
            return int(action)
        except (TypeError, ValueError):
            pass
        try:
            return tuple(round(float(x), 2) for x in action)
        except TypeError:
            return str(action)
