"""Deterministic local PCM fixtures; no microphone, playback or private inputs."""

import json
from pathlib import Path
import wave

import numpy as np

from prepare import ROOT, digest

RATE = 16000
SEED = 2007


def read_pcm(path: Path) -> np.ndarray:
    with wave.open(str(path), "rb") as audio:
        if (audio.getnchannels(), audio.getsampwidth(), audio.getframerate(), audio.getcomptype()) != (1, 2, RATE, "NONE"):
            raise ValueError(f"Expected mono 16 kHz PCM16: {path}")
        return np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2").copy()


def write_pcm(path: Path, pcm: np.ndarray) -> None:
    with wave.open(str(path), "wb") as audio:
        audio.setnchannels(1)
        audio.setsampwidth(2)
        audio.setframerate(RATE)
        audio.writeframes(pcm.astype("<i2").tobytes())


def silence(seconds: float) -> np.ndarray:
    return np.zeros(round(seconds * RATE), dtype=np.int16)


def trim(pcm: np.ndarray) -> np.ndarray:
    voiced = np.flatnonzero(np.abs(pcm.astype(np.int32)) > 200)
    if not len(voiced):
        raise ValueError("Synthetic speech fixture has no speech energy")
    return pcm[max(0, voiced[0] - 160):min(len(pcm), voiced[-1] + 161)]


def noise_mix(pcm: np.ndarray, snr_db: float, rng: np.random.Generator) -> np.ndarray:
    signal = pcm.astype(np.float64)
    noise = rng.normal(size=len(pcm))
    signal_rms = np.sqrt(np.mean(signal * signal))
    noise *= signal_rms / (10 ** (snr_db / 20) * np.sqrt(np.mean(noise * noise)))
    return np.clip(signal + noise, -32768, 32767).astype(np.int16)


def main() -> None:
    directory = ROOT / "fixtures"
    sources = directory / "source"
    provenance = json.loads((sources / "provenance.json").read_text("utf-8-sig"))
    rng = np.random.Generator(np.random.PCG64(SEED))
    cases = []

    def add(case_id, pcm, group, wake_end=None, command_start=None, voice=None, rate=None):
        path = directory / (case_id + ".wav")
        write_pcm(path, pcm)
        cases.append({
            "id": case_id, "file": path.name, "sha256": digest(path),
            "group": group, "samples": len(pcm), "seconds": len(pcm) / RATE,
            "wake_end_sample": wake_end, "command_start_sample": command_start,
            "voice": voice, "sapi_rate": rate,
        })

    for source in provenance:
        path = sources / source["file"]
        if digest(path) != source["sha256"]:
            raise ValueError(f"SAPI source digest mismatch: {path}")
    for index, source in enumerate(item for item in provenance if item["phrase_id"] == "wake"):
        prefix = source["file"].removesuffix("wake.wav")
        wake = trim(read_pcm(sources / source["file"]))
        command = trim(read_pcm(sources / (prefix + "command.wav")))
        continuous = trim(read_pcm(sources / (prefix + "immediate.wav")))
        lead = silence(0.5)
        wake_end = len(lead) + len(wake)
        immediate = np.concatenate((lead, wake, command, silence(2)))
        common = {"voice": source["voice"], "rate": source["rate"]}
        add(f"{index}-wake", np.concatenate((lead, wake, silence(6))), "wake_only", wake_end, **common)
        add(f"{index}-immediate", immediate, "immediate_spliced", wake_end, wake_end, **common)
        add(f"{index}-continuous", np.concatenate((lead, continuous, silence(2))), "continuous", **common)
        add(f"{index}-paused", np.concatenate((lead, wake, silence(0.8), command, silence(2))),
            "paused", wake_end, wake_end + len(silence(0.8)), **common)
        tone = (2000 * np.sin(2 * np.pi * 440 * np.arange(65 * RATE) / RATE)).astype(np.int16)
        add(f"{index}-long-command", np.concatenate((lead, wake, tone, silence(2))),
            "long_command_tone", wake_end, wake_end, **common)
        for snr in (10, 0):
            add(f"{index}-noise-{snr}", noise_mix(immediate, snr, rng),
                f"white_noise_{snr}dB", wake_end, wake_end, **common)
        tts = trim(read_pcm(sources / (prefix + "tts.wav")))
        add(f"{index}-tts-replay", np.concatenate((lead, tts, silence(2))), "self_tts_replay", **common)
        interruption = np.concatenate((lead, wake, command))
        offset = RATE
        mixed = np.zeros(max(len(tts), offset + len(interruption)) + 2 * RATE, dtype=np.int32)
        mixed[:len(tts)] += tts.astype(np.int32) // 2
        mixed[offset:offset + len(interruption)] += interruption.astype(np.int32) // 2
        add(f"{index}-overlap", np.clip(mixed, -32768, 32767).astype(np.int16),
            "synthetic_overlap", offset + wake_end, offset + wake_end, **common)
        negative = trim(read_pcm(sources / (prefix + "negative.wav")))
        add(f"{index}-negative", np.concatenate((lead, negative, silence(2))), "negative_speech", **common)
    add("negative-silence", silence(30), "negative_silence")
    add("negative-noise", rng.normal(0, 500, 30 * RATE).astype(np.int16), "negative_noise")
    manifest = {
        "kind": "synthetic_file_only", "sample_rate": RATE, "noise_seed": SEED,
        "noise": "PCG64 white Gaussian; not recorded office noise",
        "source_provenance": provenance, "cases": cases,
        "limitations": [
            "Two installed en-US SAPI voices are not human speakers or accent coverage",
            "Spliced zero-gap wake/command has oracle boundaries, not natural coarticulation",
            "Continuous utterance has no annotated word boundary",
            "Digital TTS replay/overlap is not real speaker-to-microphone or human interruption",
            "Generated assets stay ignored; rights to redistribute Windows voice assets are not asserted",
        ],
    }
    (directory / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Created {len(cases)} cases, {sum(case['seconds'] for case in cases):.2f} seconds")


if __name__ == "__main__":
    main()
