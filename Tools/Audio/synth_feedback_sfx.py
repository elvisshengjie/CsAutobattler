"""Procedurally synthesizes the combat/UI feedback sound effects.

Run from the repository root:
    python Tools/Audio/synth_feedback_sfx.py

Writes 16-bit mono WAVs to Assets/Resources/Audio/Feedback/, where
CombatAudioFeedback loads them with Resources.Load. Everything is generated
from oscillators and noise, so there are no third-party licences involved.
"""
import os
import wave

import numpy as np

RATE = 44100
OUT = os.path.join("Assets", "Resources", "Audio", "Feedback")
rng = np.random.default_rng(7)


def t(seconds):
    return np.arange(int(RATE * seconds)) / RATE


def env(n, attack=0.005, release=None, curve=4.0):
    """Fast attack, exponential decay envelope of length n samples."""
    x = np.arange(n) / RATE
    a = np.clip(x / max(attack, 1e-4), 0, 1)
    total = n / RATE
    d = np.exp(-curve * x / total) if release is None else np.exp(-x / release)
    return a * d


def tone(freq, seconds, kind="sine", sweep_to=None):
    x = t(seconds)
    f = freq if sweep_to is None else np.geomspace(freq, sweep_to, len(x))
    phase = 2 * np.pi * np.cumsum(f) / RATE
    if kind == "sine":
        return np.sin(phase)
    if kind == "square":
        return np.tanh(3 * np.sin(phase))
    if kind == "saw":
        return 2 * ((phase / (2 * np.pi)) % 1.0) - 1
    if kind == "tri":
        return 2 * np.abs(2 * ((phase / (2 * np.pi)) % 1.0) - 1) - 1
    raise ValueError(kind)


def noise(seconds):
    return rng.uniform(-1, 1, len(t(seconds)))


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / RATE)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[: len(p)] += p
    return out


def at(x, seconds, total):
    out = np.zeros(int(RATE * total))
    s = int(RATE * seconds)
    out[s : s + len(x)] += x[: len(out) - s]
    return out


def reverb(x, wet=0.25, decay=0.35):
    """Cheap feedback-comb reverb tail for a little space."""
    out = np.concatenate([x, np.zeros(int(RATE * decay * 2))])
    for delay, gain in ((0.029, 0.5), (0.037, 0.45), (0.051, 0.4), (0.067, 0.35)):
        d = int(RATE * delay)
        tail = np.zeros_like(out)
        for k in range(1, 8):
            g = gain ** k
            if g < 0.01 or d * k >= len(out):
                break
            tail[d * k :] += out[: len(out) - d * k] * g
        out = out + tail * wet / 4
    return out


def save(name, x, peak=0.89):
    x = np.asarray(x, dtype=np.float64)
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    fade = min(len(x), int(RATE * 0.004))
    x[-fade:] *= np.linspace(1, 0, fade)
    data = (x * 32767).astype("<i2")
    os.makedirs(OUT, exist_ok=True)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(data.tobytes())
    print(f"{name}.wav  {len(x) / RATE:.2f}s")


def note(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


# --- UI -------------------------------------------------------------------
def ui_click():
    s = tone(2400, 0.035, "sine", 1600) * env(len(t(0.035)), 0.001, 0.008)
    return mix(s, highpass(noise(0.02), 3000) * env(len(t(0.02)), 0.0005, 0.004) * 0.4)


def ui_buy():
    total = 0.45
    a = tone(note(84), 0.25, "tri") * env(len(t(0.25)), 0.002, 0.07)
    b = tone(note(91), 0.35, "tri") * env(len(t(0.35)), 0.002, 0.1)
    shimmer = tone(note(103), 0.3) * env(len(t(0.3)), 0.002, 0.06) * 0.25
    return reverb(mix(at(a, 0, total), at(mix(b, shimmer), 0.06, total)), 0.4)


def ui_sell():
    total = 0.4
    a = tone(note(88), 0.2, "tri") * env(len(t(0.2)), 0.002, 0.06)
    b = tone(note(81), 0.3, "tri") * env(len(t(0.3)), 0.002, 0.08)
    return reverb(mix(at(a, 0, total), at(b, 0.07, total)), 0.3)


def ui_reroll():
    n = noise(0.32)
    sweep = lowpass(n, 2500) * env(len(n), 0.12, curve=6)
    ticks = mix(*[at(tone(1800 + 300 * i, 0.03) * env(len(t(0.03)), 0.001, 0.006), 0.04 * i, 0.35)
                  for i in range(6)])
    return mix(sweep * 0.7, ticks * 0.6)


def ui_deny():
    s = tone(150, 0.22, "square") * env(len(t(0.22)), 0.003, curve=3)
    s2 = tone(110, 0.22, "square") * env(len(t(0.22)), 0.003, curve=3)
    return lowpass(s + s2, 1800)


def rank_up():
    total = 1.3
    parts = []
    for i, m in enumerate((72, 76, 79, 84, 88)):
        n = mix(tone(note(m), 0.6, "tri") * env(len(t(0.6)), 0.003, 0.18),
                tone(note(m + 12), 0.5) * env(len(t(0.5)), 0.003, 0.1) * 0.3)
        parts.append(at(n, i * 0.07, total))
    riser = lowpass(noise(0.4), 6000) * np.linspace(0, 1, len(t(0.4))) ** 2 * 0.25
    impact = tone(note(48), 0.7, "sine") * env(len(t(0.7)), 0.002, 0.25) * 0.8
    return reverb(mix(*parts, at(riser, 0, total), at(impact, 0.35, total)), 0.5, 0.6)


# --- Combat ---------------------------------------------------------------
def kill_confirm():
    """Crisp hit-marker 'tick' over a low body thump."""
    tick = mix(tone(3200, 0.05, "sine", 2200) * env(len(t(0.05)), 0.0005, 0.01),
               highpass(noise(0.03), 4000) * env(len(t(0.03)), 0.0003, 0.005) * 0.5)
    thump = tone(110, 0.25, "sine", 45) * env(len(t(0.25)), 0.001, 0.07)
    bell = tone(note(93), 0.35) * env(len(t(0.35)), 0.002, 0.09) * 0.35
    return mix(tick, thump * 0.9, at(bell, 0.02, 0.4))


def multikill():
    total = 1.0
    hit = lowpass(noise(0.5), 1500) * env(len(t(0.5)), 0.001, 0.12)
    sub = tone(80, 0.8, "sine", 38) * env(len(t(0.8)), 0.002, 0.3)
    stab = mix(*[tone(note(m), 0.7, "saw") * env(len(t(0.7)), 0.004, 0.22) for m in (62, 69, 74, 78)])
    stab = lowpass(stab, 3500) * 0.35
    return reverb(mix(hit * 0.6, sub, at(stab, 0.01, total)), 0.45, 0.5)


def ally_down():
    total = 0.9
    thud = lowpass(noise(0.3), 500) * env(len(t(0.3)), 0.001, 0.08)
    fall = tone(note(64), 0.7, "tri", note(52)) * env(len(t(0.7)), 0.005, 0.25) * 0.5
    low = tone(70, 0.6, "sine", 40) * env(len(t(0.6)), 0.002, 0.2)
    return reverb(mix(thud, low, at(fall, 0.03, total)), 0.4)


def heartbeat():
    total = 0.85
    def beat(f, g):
        x = tone(f, 0.18, "sine", f * 0.6) * env(len(t(0.18)), 0.004, 0.04)
        return lowpass(x, 300) * g
    return mix(at(beat(70, 1.0), 0, total), at(beat(60, 0.7), 0.17, total))


def bomb_beep():
    s = tone(2900, 0.09, "square") * env(len(t(0.09)), 0.001, 0.05)
    return lowpass(s, 6000)


def round_start():
    total = 1.4
    riser = lowpass(noise(1.0), 3000) * np.linspace(0, 1, len(t(1.0))) ** 3
    hit = tone(55, 0.9, "sine", 35) * env(len(t(0.9)), 0.002, 0.35)
    crack = highpass(noise(0.15), 1500) * env(len(t(0.15)), 0.0005, 0.03)
    brass = lowpass(mix(*[tone(note(m), 0.6, "saw") for m in (38, 45, 50)]), 900) * env(len(t(0.6)), 0.01, 0.25)
    return reverb(mix(riser * 0.35, at(mix(hit, crack * 0.5, brass * 0.3), 1.0, total)), 0.4, 0.6)


def round_win():
    total = 2.2
    seq = [(60, 0.0), (64, 0.12), (67, 0.24), (72, 0.36)]
    parts = [at(lowpass(tone(note(m), 0.5, "saw"), 3000) * env(len(t(0.5)), 0.005, 0.15) * 0.5, s, total)
             for m, s in seq]
    chord = mix(*[tone(note(m), 1.6, "saw") for m in (60, 64, 67, 72, 76)])
    chord = lowpass(chord, 2800) * env(len(t(1.6)), 0.02, 0.6) * 0.4
    sub = tone(note(36), 1.4) * env(len(t(1.4)), 0.005, 0.5)
    return reverb(mix(*parts, at(mix(chord, sub), 0.48, total)), 0.5, 0.8)


def round_lose():
    total = 2.4
    seq = [(64, 0.0), (63, 0.3), (60, 0.6)]
    parts = [at(lowpass(tone(note(m), 0.7, "saw"), 1600) * env(len(t(0.7)), 0.01, 0.3) * 0.5, s, total)
             for m, s in seq]
    chord = mix(*[tone(note(m), 1.8, "saw") for m in (45, 48, 52, 57)])
    chord = lowpass(chord, 1200) * env(len(t(1.8)), 0.03, 0.8) * 0.45
    boom = tone(50, 1.2, "sine", 30) * env(len(t(1.2)), 0.002, 0.5)
    return reverb(mix(*parts, at(mix(chord, boom), 0.85, total)), 0.55, 0.9)


if __name__ == "__main__":
    for fn in (ui_click, ui_buy, ui_sell, ui_reroll, ui_deny, rank_up,
               kill_confirm, multikill, ally_down, heartbeat, bomb_beep,
               round_start, round_win, round_lose):
        save(fn.__name__, fn())
