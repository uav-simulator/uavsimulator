import torch, time

torch.manual_seed(0)

class TinyCNN(torch.nn.Module):
    def __init__(self):
        super().__init__()
        self.cnn = torch.nn.Sequential(
            torch.nn.Conv2d(3, 32, 8, 4), torch.nn.ReLU(),
            torch.nn.Conv2d(32, 64, 4, 2), torch.nn.ReLU(),
            torch.nn.Conv2d(64, 64, 3, 1), torch.nn.ReLU(),
            torch.nn.Flatten(),
            torch.nn.Linear(3136, 256), torch.nn.ReLU(),
            torch.nn.Linear(256, 5),
        )
    def forward(self, x):
        return self.cnn(x)

for dev in ["cpu", "cuda"]:
    m = TinyCNN().to(dev)
    opt = torch.optim.Adam(m.parameters(), lr=1e-3)
    for _ in range(3):  # warmup
        x = torch.randn(64, 3, 84, 84, device=dev)
        y = torch.randint(0, 5, (64,), device=dev)
        loss = torch.nn.functional.cross_entropy(m(x), y)
        opt.zero_grad(); loss.backward(); opt.step()
    if dev == "cuda":
        torch.cuda.synchronize()
    t = time.perf_counter()
    for _ in range(100):
        x = torch.randn(64, 3, 84, 84, device=dev)
        y = torch.randint(0, 5, (64,), device=dev)
        loss = torch.nn.functional.cross_entropy(m(x), y)
        opt.zero_grad(); loss.backward(); opt.step()
    if dev == "cuda":
        torch.cuda.synchronize()
    el = time.perf_counter() - t
    print(f"{dev}: 100 iters batch=64 PPO-style CNN: {el*1000:.0f}ms ({100/el:.0f} iter/s, {64*100/el:.0f} samples/s)")
