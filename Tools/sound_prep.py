# Cuts and cleans the recorded CC0 sounds into game-ready mono 44.1 kHz loops / one-shots.
import os
import numpy as np
import soundfile as sf

OUT = 'game'
os.makedirs(OUT, exist_ok=True)
SR = 44100


def load(path):
    d, sr = sf.read(path, always_2d=True)
    m = d.mean(1)
    if sr != SR:  # FFT resample (band-limited)
        n = int(round(len(m) * SR / sr))
        F = np.fft.rfft(m)
        F = F[: n // 2 + 1] if len(F) > n // 2 + 1 else np.pad(F, (0, n // 2 + 1 - len(F)))
        m = np.fft.irfft(F, n) * (n / len(d))
    return m


def highpass(x, hz=25):
    # One-pole DC / rumble blocker.
    a = np.exp(-2 * np.pi * hz / SR)
    y = np.zeros_like(x)
    prev_x = prev_y = 0.0
    for i, v in enumerate(x):
        prev_y = a * (prev_y + v - prev_x)
        prev_x = v
        y[i] = prev_y
    return y


def loop(x, a, b, fade):
    # Seamless loop from x[a:b]: the extra 'fade' seconds after b are cross-faded into the start.
    s, e, f = int(a * SR), int(b * SR), int(fade * SR)
    body = x[s:e].copy()
    tail = x[e:e + f]
    w = np.sin(np.linspace(0, np.pi / 2, f)) ** 2
    body[:f] = body[:f] * w + tail * (1 - w)
    return body


def norm(x, rms):
    return x * (rms / np.sqrt((x ** 2).mean()))


def save(name, x, peak=0.95):
    x = np.clip(x, -peak, peak)
    sf.write(os.path.join(OUT, name), x.astype(np.float32), SR, subtype='PCM_16')
    print(name, f'{len(x) / SR:.2f}s peak={np.abs(x).max():.2f} rms={np.sqrt((x ** 2).mean()):.3f}')


fs = 'fs/'
idle = highpass(load(fs + '369054__andrewalexander__car_idle_ext_loop.wav'))
save('engine_idle.wav', norm(loop(idle, 0.0, 4.0, 0.3), 0.16))

mid = highpass(load(fs + '748027__dmitry_mansurev64__sedan-engine-loop.ogg'))
save('engine_mid.wav', norm(loop(mid, 1.0, 9.0, 0.4), 0.16))

rev = highpass(load(fs + '181460__erik90__car-rev.wav'))
save('engine_high.wav', norm(loop(rev, 23.0, 25.5, 0.25), 0.16))

squeal = highpass(load(fs + '71737__audible-edge__chrysler-lhs-tire-squeal-02-04-25-2009.wav'), 120)
save('tire_squeal.wav', norm(loop(squeal, 4.1, 6.6, 0.3), 0.1))

crash = load(fs + '151624__qubodup__clank-car-crash-collision.wav')
crash = crash / np.abs(crash).max() * 0.9
save('crash.wav', crash)
