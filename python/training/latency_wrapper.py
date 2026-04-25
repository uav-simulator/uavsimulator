"""Action latency wrapper for matching real-robot inference loop timing.

Real KS0223 deployment: AutopilotService loop runs at ~140ms (7Hz). The model's
decision at observation O_t is applied to action A_t which lands one tick later.
During training in Unity sim with `time_scale=10` and synchronous step calls,
there is no such delay — model gets fresh obs and action takes effect instantly.

This wrapper queues actions with N-step delay so policy learns under the same
"perception-to-actuation lag" as real deployment. With delay_steps=1 and a
140ms training tick rate, the simulated lag matches real conditions reasonably
well.

The first `delay_steps` calls after reset use a "neutral" action (Discrete: 0
== DirStop; continuous: zeros) since there's nothing in the queue yet.
"""

from __future__ import annotations

from collections import deque

import gymnasium as gym
import numpy as np
from gymnasium import spaces


class DelayedActionWrapper(gym.Wrapper):
    """Apply each action `delay_steps` steps later than agent submitted it."""

    def __init__(self, env: gym.Env, *, delay_steps: int = 1):
        super().__init__(env)
        if delay_steps < 0:
            raise ValueError(f"delay_steps must be >= 0, got {delay_steps}")
        self.delay_steps = delay_steps
        self._queue: deque = deque(maxlen=delay_steps + 1)

    def reset(self, **kwargs):
        self._queue.clear()
        return self.env.reset(**kwargs)

    def step(self, action):
        # Push current action onto the queue
        self._queue.append(action)

        # Compute the action to actually execute (delayed)
        if len(self._queue) > self.delay_steps:
            executed = self._queue.popleft()
        else:
            # Queue not yet full; emit neutral action
            executed = self._neutral_action()

        return self.env.step(executed)

    def _neutral_action(self):
        """Neutral / zero action matching the underlying action_space."""
        space = self.env.action_space
        if isinstance(space, spaces.Discrete):
            return 0  # DirStop in our discrete encoding
        if isinstance(space, spaces.Box):
            return np.zeros(space.shape, dtype=space.dtype)
        # Fallback: try to sample a 0-like value
        return space.sample()
