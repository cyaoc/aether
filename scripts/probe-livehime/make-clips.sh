#!/bin/sh
# Makes the probe's test clips in clips/; needs ffmpeg with libx264, libvpx and libopus.
# Each clip has its own tone, so you can tell by ear which one is playing.
set -e
cd "$(dirname "$0")"
mkdir -p clips
size=1280x720
ff() { ffmpeg -v error -y "$@"; }
# Distance from a ball that swings left and right across the middle.
d='hypot(X-W/2-400*sin(2*T),Y-H/2)'

# 1. H.264 + AAC test pattern with a 1 kHz tone: can it decode H.264, does sound autoplay?
ff -f lavfi -i "testsrc2=s=$size:r=30:d=5" -f lavfi -i "sine=f=1000:d=5" \
  -c:v libx264 -pix_fmt yuv420p -c:a aac -movflags +faststart -shortest clips/1-h264.mp4

# 2. VP9 with alpha + Opus: an orange ball with a soft edge on a transparent background.
ff -f lavfi -i "color=black:s=$size:r=30:d=5,format=rgba,geq=r=255:g=140:b=40:a='255*clip((140-$d)/20,0,1)'" \
  -f lavfi -i "sine=f=660:d=5" \
  -c:v libvpx-vp9 -pix_fmt yuva420p -auto-alt-ref 0 -b:v 2M -c:a libopus -shortest clips/2-alpha.webm

# 3. H.264 green screen: a pink ball on pure green, keyed by the page.
ff -f lavfi -i "color=black:s=$size:r=30:d=5,format=rgb24,geq=r='if(lt($d,120),255,0)':g='if(lt($d,120),105,255)':b='if(lt($d,120),180,0)'" \
  -f lavfi -i "sine=f=440:d=5" \
  -c:v libx264 -pix_fmt yuv420p -c:a aac -movflags +faststart -shortest clips/3-ball.green.mp4

# 4. H.264 left-right split: the ball's colour on the left, its alpha as grey on the right.
ff -f lavfi -i "color=black:s=$size:r=30:d=5,format=rgba,geq=r=255:g=140:b=40:a='255*clip((140-$d)/20,0,1)'" \
  -f lavfi -i "sine=f=550:d=5" \
  -filter_complex "[0]split[a][b];[a]format=rgb24[c];[b]alphaextract,format=rgb24[m];[c][m]hstack[v]" \
  -map "[v]" -map 1 -c:v libx264 -pix_fmt yuv420p -c:a aac -movflags +faststart -shortest clips/4-ball.split.mp4

# 5. H.264 black background: a glow whose brightness becomes opacity.
g="exp(-pow($d,2)/12800)"
ff -f lavfi -i "color=black:s=$size:r=30:d=5,format=rgb24,geq=r='255*$g':g='200*$g':b='80*$g'" \
  -f lavfi -i "sine=f=880:d=5" \
  -c:v libx264 -pix_fmt yuv420p -c:a aac -movflags +faststart -shortest clips/5-glow.black.mp4

# 6, 7. Other shapes: a portrait clip and a small one, to see how the page fits them into the source.
for shape in 6-portrait:720x1280 7-small:640x360; do
  ff -f lavfi -i "testsrc2=s=${shape#*:}:r=30:d=5" -f lavfi -i "sine=f=1000:d=5" \
    -c:v libx264 -pix_fmt yuv420p -c:a aac -movflags +faststart -shortest "clips/${shape%%:*}.mp4"
done

ls -l clips
