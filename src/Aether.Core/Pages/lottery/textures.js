'use strict';
const textures = (() => {
  const images = new Map(), tintedImages = new Map();
  const ready = Promise.all([...document.querySelectorAll('link[rel="preload"][as="image"]')].map(async link => {
    const image = new Image();
    image.src = link.href;
    try {
      await image.decode();
      images.set(link.href.split('/textures/')[1].replace(/\.webp$/, ''), image);
    } catch { /* Keep vector particles if a local image cannot be decoded. */ }
  }));
  return {
    ready,
    image:name => images.get(name),
    confettiCell(shape, color, grade) {
      const row = ({'#ff7aae':0,'#ffb3cf':0,'#ffd45e':1,'#5fd3b0':2,'#bff0df':2,'#a991ff':3,'#7ccbff':3})[color.toLowerCase()] ?? (grade === 'Third' ? 2 : 3);
      return [({heart:0,star:1,strip:2,confetti:2,candy:3,dot:3})[shape] * 96,row * 96,96,96];
    },
    tinted(name, color, width, height = width) {
      const image = images.get(name);
      if (!image) return null;
      const key = `${name} ${color} ${width} ${height}`;
      if (!tintedImages.has(key)) {
        const canvas = document.createElement('canvas');
        canvas.width = width; canvas.height = height;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(image,0,0,width,height);
        ctx.globalCompositeOperation = 'source-in'; ctx.fillStyle = color; ctx.fillRect(0,0,width,height);
        ctx.globalCompositeOperation = 'lighter'; ctx.globalAlpha = .6; ctx.drawImage(image,0,0,width,height);
        tintedImages.set(key,canvas);
      }
      return tintedImages.get(key);
    },
  };
})();
