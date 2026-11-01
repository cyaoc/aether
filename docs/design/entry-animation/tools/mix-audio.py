# Mixes each tier's sound track from audio/cues.json (needs ffmpeg): python3 tools/mix-audio.py
# A cue: file, from and length in seconds within it, fadeOut, at on the tier's timeline, peak in dB after gain.
import json
import pathlib
import re
import subprocess

audio = pathlib.Path(__file__).resolve().parent.parent / 'audio'


def peak(file, start, length):
    log = subprocess.run(['ffmpeg', '-hide_banner', '-nostats', '-ss', str(start), '-t', str(length), '-i', str(file),
                          '-af', 'volumedetect', '-f', 'null', '-'], capture_output=True, text=True).stderr
    return float(re.search(r'max_volume: (-?[\d.]+) dB', log).group(1))


for tier, cues in json.loads((audio / 'cues.json').read_text(encoding='utf8')).items():
    args, chains = ['ffmpeg', '-loglevel', 'error', '-y'], []
    for n, cue in enumerate(cues):
        file = audio / 'sources' / cue['file']
        gain = cue['peak'] - peak(file, cue['from'], cue['length'])
        args += ['-ss', str(cue['from']), '-t', str(cue['length']), '-i', str(file)]
        chains.append(f"[{n}:a]aresample=48000,aformat=channel_layouts=stereo,afade=t=in:d=0.005,"
                      f"afade=t=out:st={cue['length'] - cue['fadeOut']}:d={cue['fadeOut']},volume={gain}dB,"
                      f"adelay={round(cue['at'] * 1000)}:all=1[l{n}]")
    inputs = ''.join(f'[l{n}]' for n in range(len(cues)))
    chains.append(f'{inputs}amix=inputs={len(cues)}:normalize=0:duration=longest,alimiter=limit=0.9[out]')
    subprocess.run(args + ['-filter_complex', ';'.join(chains), '-map', '[out]', '-c:a', 'libopus', '-b:a', '160k',
                           str(audio / f'tier-{tier}.ogg')], check=True)
    print(f'tier-{tier}.ogg')
