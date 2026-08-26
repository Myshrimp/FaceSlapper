# -*- coding: utf-8 -*-
"""
程序化生成游戏音效（WAV，16-bit PCM 单声道 22050Hz，无需外部音频素材）：
  - Assets/Resources/Audio/SfxHit.wav   拳击命中闷响（低频冲击 + 短噪声）
  - Assets/Resources/Audio/SfxBonk.wav  撞墙眩晕 "bonk"（下滑音 + 起始爆点）
用法：python Tools/GenerateSfx.py   （在工程根目录执行，幂等可重复）
"""
import math
import os
import random
import struct
import wave

OUT_DIR = os.path.join("Assets", "Resources", "Audio")
SAMPLE_RATE = 22050

def write_wav(path, samples):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    peak = max(max(samples), -min(samples), 1e-6)
    scale = 0.85 / peak  # 归一化防削波
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SAMPLE_RATE)
        w.writeframes(b"".join(
            struct.pack("<h", int(max(-1.0, min(1.0, s * scale)) * 32767))
            for s in samples))
    print("  %-14s %5.2fs -> %s" % (os.path.basename(path), len(samples) / SAMPLE_RATE, path))

def gen_hit(duration=0.22):
    """拳击闷响：80Hz 低频冲击指数衰减 + 前 25ms 白噪声爆点 + 二次谐波增厚。"""
    random.seed(7)
    n = int(SAMPLE_RATE * duration)
    out = []
    for i in range(n):
        t = i / SAMPLE_RATE
        thump = math.sin(2 * math.pi * 80 * t) * math.exp(-t * 28)
        thump += 0.4 * math.sin(2 * math.pi * 160 * t) * math.exp(-t * 40)
        noise = (random.uniform(-1, 1)) * math.exp(-t * 160) if t < 0.025 else 0.0
        out.append(thump + 0.6 * noise)
    return out

def gen_bonk(duration=0.45):
    """撞墙 bonk：700Hz -> 180Hz 下滑音（带三谐波）指数衰减 + 起始爆点。"""
    random.seed(11)
    n = int(SAMPLE_RATE * duration)
    out = []
    phase = 0.0
    for i in range(n):
        t = i / SAMPLE_RATE
        freq = 180 + (700 - 180) * math.exp(-t * 9)   # 指数下滑
        phase += 2 * math.pi * freq / SAMPLE_RATE
        tone = math.sin(phase) + 0.3 * math.sin(3 * phase)
        env = math.exp(-t * 8)
        noise = (random.uniform(-1, 1)) * math.exp(-t * 200) if t < 0.02 else 0.0
        out.append(tone * env + 0.5 * noise)
    return out

def main():
    print("生成音效 -> %s" % OUT_DIR)
    write_wav(os.path.join(OUT_DIR, "SfxHit.wav"), gen_hit())
    write_wav(os.path.join(OUT_DIR, "SfxBonk.wav"), gen_bonk())

if __name__ == "__main__":
    main()
