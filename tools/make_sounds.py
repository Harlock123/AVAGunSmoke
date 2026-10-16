#!/usr/bin/env python3
"""Synthesises AVAGunSmoke's sound effects and tunes into ../src/AVAGunSmoke/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAGunSmoke", "Sounds")
RNG = random.Random(1985)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def hooves(bpm, bars):
    """Clip-clop: two knocks on every beat, a high wood block and a lower one, the trot under the trail tune."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars * 4):
        a = tone(0.05, 1300, 900, "tri", attack=0.001, release=2, gain=0.55)
        b = tone(0.05, 900, 600, "tri", attack=0.001, release=2, gain=0.45)
        out.extend(cat(a, silence(beat / 2 - 0.05), b, silence(beat / 2 - 0.05)))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The six-guns: a sharp crack and a little ring. The outlaws' shots: duller.
    write("Fire.wav", mix(tone(0.1, 2600, 700, "noise", attack=0.001, release=1.4), tone(0.12, 520, 300, "square", release=1.2, gain=0.35)), 0.26)
    write("EnemyFire.wav", mix(tone(0.12, 1500, 450, "noise", attack=0.001, release=1.1), tone(0.1, 300, 140, "square", release=1, gain=0.4)), 0.24)
    # The ricochet: the whine of a bullet glancing off.
    write("Ricochet.wav", tone(0.32, 3400, 1300, "tri", attack=0.002, release=0.7, vibrato=0.03, vib_rate=30), 0.2)
    write("Throw.wav", tone(0.25, 400, 1200, "noise", attack=0.04, release=0.8), 0.16)
    write("Blast.wav", mix(tone(0.75, 900, 40, "noise", attack=0.002, release=0.35), tone(0.6, 110, 35, "sine", release=0.4, gain=0.9)), 0.5)
    write("OutlawDown.wav", cat(tone(0.06, 700, 500, "square", release=0.6), tone(0.16, 420, 160, "tri", release=0.6)), 0.26)
    write("BarrelBreak.wav", mix(tone(0.3, 800, 200, "noise", attack=0.001, release=0.6), tone(0.25, 220, 90, "square", release=0.6, gain=0.4)), 0.4)
    write("PowerUp.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["D5", "F#5", "A5", "D6"]]), 0.34)
    # Hoofbeats: a quick gallop.
    write("Gallop.wav", cat(*[cat(tone(0.04, 700, 500, "tri", attack=0.001, release=2), silence(0.05), tone(0.04, 600, 450, "tri", attack=0.001, release=2), silence(0.05), tone(0.04, 650, 480, "tri", attack=0.001, release=2), silence(0.12)) for _ in range(3)]), 0.4)
    write("BossHit.wav", mix(tone(0.12, 500, 200, "square", release=0.8), tone(0.08, 1800, 600, "noise", release=1)), 0.3)
    write("BossDown.wav", tune(150, [("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 1), ("A5", 0.5), ("D6", 2)], [("D3", 1), ("A2", 1), ("D3", 1), ("A2", 0.5), ("D3", 1.5)], kind="pulse"), 0.42)
    # The showdown: a long, wavering harmonica-ish chord, then a bell.
    write("Showdown.wav", cat(mix(tone(1.2, hz("D4"), kind="saw", attack=0.1, release=0.6, vibrato=0.02, vib_rate=6, gain=0.5), tone(1.2, hz("A4"), kind="saw", attack=0.1, release=0.6, vibrato=0.02, vib_rate=6, gain=0.4)), tone(0.8, hz("D6"), kind="sine", release=1.5, gain=0.7)), 0.4)
    write("Death.wav", cat(mix(tone(0.4, 1000, 60, "noise", release=0.4), tone(0.4, 400, 80, "saw", release=0.4, gain=0.5)), *[tone(0.16, hz(n), kind="tri", release=0.4) for n in ["A4", "F4", "D4", "A3"]]), 0.46)
    write("StageStart.wav", cat(*[tone(d, hz(n), kind="pulse", release=0.3) for n, d in [("A4", 0.1), ("D5", 0.1), ("F#5", 0.1), ("A5", 0.3)]]), 0.3)
    write("StageClear.wav", tune(140, [("A4", 0.5), ("D5", 0.5), ("F#5", 0.5), ("A5", 1), ("F#5", 0.5), ("A5", 0.5), ("D6", 2)], [("D3", 1), ("A2", 1), ("D3", 1), ("A2", 0.5), ("D3", 2)], kind="pulse"), 0.42)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["D6", "A6", "D6", "A6", "D7"]]), 0.38)

    # The trail tune while you walk: a loping cowboy melody over a bass and the clip-clop of hooves (an original
    # composition), a whole number of beats long so it loops cleanly.
    lead = [("A4", 1), ("D5", 0.5), ("E5", 0.5), ("F#5", 1.5), ("E5", 0.5),
            ("D5", 1), ("B4", 1), ("A4", 2),
            ("A4", 1), ("D5", 0.5), ("F#5", 0.5), ("A5", 1.5), ("G5", 0.5),
            ("F#5", 1), ("E5", 1), ("D5", 2)]
    bass = [("D3", 1), ("A2", 1)] * 2 + [("G2", 1), ("D3", 1), ("D3", 1), ("A2", 1)] + [("D3", 1), ("A2", 1)] * 2 + [("A2", 1), ("E3", 1), ("D3", 1), ("A2", 1)]
    melody = tune(108, lead, bass, kind="pulse")
    write("Music.wav", mix([v * 0.6 for v in melody], hooves(108, 4)), 0.3)

    title_lead = lead + [("F#5", 1), ("F#5", 0.5), ("G5", 0.5), ("A5", 1), ("F#5", 1), ("E5", 1), ("D5", 0.5), ("E5", 0.5), ("F#5", 2),
                         ("D5", 1), ("F#5", 0.5), ("A5", 0.5), ("D6", 1.5), ("C#6", 0.5), ("B5", 0.5), ("A5", 0.5), ("G5", 0.5), ("E5", 0.5), ("D5", 2)]
    title_bass = bass + [("D3", 1), ("A2", 1)] * 2 + [("A2", 1), ("E3", 1)] * 2 + [("B2", 1), ("F#2", 1), ("G2", 1), ("D3", 1), ("A2", 1), ("A2", 1), ("D3", 2)]
    write("TitleTune.wav", cat(mix(tune(112, title_lead, title_bass, kind="pulse"), hooves(112, 8)), silence(1.0)), 0.42)
    write("StartTune.wav", tune(150, [("A4", 0.5), ("D5", 0.5), ("F#5", 0.5), ("A5", 1), ("F#5", 0.5), ("A5", 1)], [("D3", 1), ("A2", 1), ("D3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(60, [("A4", 1), ("F#4", 1), ("E4", 1), ("C#4", 1), ("D4", 4)], [("D3", 2), ("B2", 2), ("A2", 2), ("D2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
