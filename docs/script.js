/* ── Three.js — cyber grid + particles ── */
(function () {
  const canvas = document.getElementById('bg-canvas');
  if (!canvas || typeof THREE === 'undefined') return;

  const mobile = window.innerWidth < 768;
  const lowEnd  = window.innerWidth < 480;

  const renderer = new THREE.WebGLRenderer({ canvas, alpha: false, antialias: !mobile });
  renderer.setPixelRatio(Math.min(devicePixelRatio, mobile ? 1 : 1.5));
  renderer.setClearColor(0x07080f, 1);

  const scene = new THREE.Scene();
  scene.fog = new THREE.FogExp2(0x07080f, mobile ? 0.030 : 0.022);

  const camera = new THREE.PerspectiveCamera(44, 1, 0.1, 80);
  camera.position.set(0, 4.2, 8.0);
  camera.lookAt(0, -0.5, -5);

  // ── Main grid — vertex colors map height to dark-navy → cyan → white ─────
  const S1 = lowEnd ? 20 : mobile ? 30 : 60;
  const geo1 = new THREE.PlaneGeometry(50, 70, S1, S1);
  geo1.rotateX(-Math.PI / 2);

  const vcBuf  = new Float32Array(geo1.attributes.position.count * 3);
  const vcAttr = new THREE.BufferAttribute(vcBuf, 3);
  geo1.setAttribute('color', vcAttr);

  const mesh1 = new THREE.Mesh(geo1, new THREE.MeshBasicMaterial({
    vertexColors: true, wireframe: true, transparent: true,
    opacity: mobile ? 0.55 : 0.68,
  }));
  mesh1.position.set(0, -1.2, -6);
  scene.add(mesh1);

  const pos1  = geo1.attributes.position;
  const base1 = new Float32Array(pos1.count);
  for (let i = 0; i < pos1.count; i++) base1[i] = pos1.getY(i);

  // ── Far sparse grid (parallax depth, ambient-only motion) ─────────────────
  let farPos = null, farBase = null;
  if (!mobile) {
    const geo2 = new THREE.PlaneGeometry(80, 100, 22, 22);
    geo2.rotateX(-Math.PI / 2);
    const mesh2 = new THREE.Mesh(geo2, new THREE.MeshBasicMaterial({
      color: 0x091e3a, wireframe: true, transparent: true, opacity: 0.09,
    }));
    mesh2.position.set(0, -4.0, -18);
    scene.add(mesh2);
    farPos  = geo2.attributes.position;
    farBase = new Float32Array(farPos.count);
    for (let i = 0; i < farPos.count; i++) farBase[i] = farPos.getY(i);
  }

  // ── Floating particles — cyan embers drifting upward ──────────────────────
  const PC = mobile ? 50 : 130;
  const pp  = new Float32Array(PC * 3);
  const pv  = new Float32Array(PC * 3);
  const pcol = new Float32Array(PC * 3);

  function resetParticle(i, scatterY) {
    pp[i*3]   = (Math.random() - 0.5) * 38;
    pp[i*3+1] = scatterY ? Math.random() * 12 - 2 : -2.5;
    pp[i*3+2] = Math.random() * -28 - 2;
    pv[i*3]   = (Math.random() - 0.5) * 0.0018;
    pv[i*3+1] = Math.random() * 0.0028 + 0.0008;
    pv[i*3+2] = (Math.random() - 0.5) * 0.0012;
    const b = Math.random();
    pcol[i*3]   = b * 0.15 + 0.08;
    pcol[i*3+1] = b * 0.10 + 0.88;
    pcol[i*3+2] = 1.0;
  }
  for (let i = 0; i < PC; i++) resetParticle(i, true);

  const pGeo = new THREE.BufferGeometry();
  pGeo.setAttribute('position', new THREE.BufferAttribute(pp,   3));
  pGeo.setAttribute('color',    new THREE.BufferAttribute(pcol, 3));
  scene.add(new THREE.Points(pGeo, new THREE.PointsMaterial({
    vertexColors: true, size: 1.5, sizeAttenuation: true,
    transparent: true, opacity: 0.52,
  })));

  // ── Wave functions ────────────────────────────────────────────────────────
  function ambientWave(x, z, t) {
    return Math.sin(x * 0.28 + t * 0.78) * 0.42
         + Math.sin(z * 0.18 + t * 0.53) * 0.30
         + Math.sin((x - z) * 0.12 + t * 0.37) * 0.18
         + Math.sin((x + z) * 0.062 + t * 0.21) * 0.11;
  }

  function sonarPulse(x, z, t) {
    const d = Math.hypot(x, z);
    return Math.max(0, Math.sin(t * 2.1 - d * 0.27)) * Math.exp(-d * 0.058) * 1.15;
  }

  // Height → color: dark-navy (#091826) → teal (#107fa0) → cyan (#00e5ff) → white
  function applyColor(buf, idx, h) {
    const n = Math.max(0, Math.min(1, (h + 1.0) / 3.0));
    let r, g, b;
    if (n < 0.45) {
      const s = n / 0.45;
      r = 0.035 + s * 0.030;
      g = 0.095 + s * 0.400;
      b = 0.150 + s * 0.480;
    } else if (n < 0.78) {
      const s = (n - 0.45) / 0.33;
      r = 0.065 + s * 0.180;
      g = 0.495 + s * 0.400;
      b = 0.630 + s * 0.260;
    } else {
      const s = (n - 0.78) / 0.22;
      r = 0.245 + s * 0.755;
      g = 0.895 + s * 0.105;
      b = 0.890 + s * 0.110;
    }
    buf[idx] = r; buf[idx+1] = g; buf[idx+2] = b;
  }

  // ── Resize ────────────────────────────────────────────────────────────────
  function resize() {
    renderer.setSize(window.innerWidth, window.innerHeight, false);
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
  }
  window.addEventListener('resize', resize, { passive: true });
  resize();

  let paused = false;
  document.addEventListener('visibilitychange', () => {
    paused = document.hidden;
    if (!paused) tick();
  });

  let t = 0;
  function tick() {
    if (paused) return;
    requestAnimationFrame(tick);
    t += mobile ? 0.0016 : 0.0018;

    // Main grid: heights + vertex colors
    for (let i = 0; i < pos1.count; i++) {
      const x = pos1.getX(i), z = pos1.getZ(i);
      const y = base1[i] + ambientWave(x, z, t) + (mobile ? 0 : sonarPulse(x, z, t));
      pos1.setY(i, y);
      applyColor(vcBuf, i * 3, y);
    }
    pos1.needsUpdate = true;
    vcAttr.needsUpdate = true;

    // Far grid
    if (farPos && farBase) {
      for (let i = 0; i < farPos.count; i++)
        farPos.setY(i, farBase[i] + ambientWave(farPos.getX(i), farPos.getZ(i), t * 0.38) * 0.44);
      farPos.needsUpdate = true;
    }

    // Particles: drift upward, wrap at ceiling
    const ppa = pGeo.attributes.position.array;
    for (let i = 0; i < PC; i++) {
      ppa[i*3]   += pv[i*3];
      ppa[i*3+1] += pv[i*3+1];
      ppa[i*3+2] += pv[i*3+2];
      if (ppa[i*3+1] > 9) resetParticle(i, false);
    }
    pGeo.attributes.position.needsUpdate = true;

    // Camera: dual-frequency sway on X + gentle pitch oscillation on Y
    camera.position.x = Math.sin(t * 0.110) * 0.45 + Math.sin(t * 0.037) * 0.12;
    camera.position.y = 4.2 + Math.sin(t * 0.072) * 0.18 + Math.sin(t * 0.041) * 0.07;
    camera.lookAt(Math.sin(t * 0.090) * 0.15, -0.5, -5);

    renderer.render(scene, camera);
  }
  tick();
})();

/* ── Background music — starts on first interaction ── */
(function () {
  const audio = document.getElementById('bg-music');
  if (!audio) return;
  audio.volume = 0.28;

  function tryPlay() { audio.play().catch(() => {}); }

  tryPlay();
  document.addEventListener('click',      tryPlay, { once: true, passive: true });
  document.addEventListener('keydown',    tryPlay, { once: true, passive: true });
  document.addEventListener('touchstart', tryPlay, { once: true, passive: true });
})();

/* ── Nav scroll state ── */
window.addEventListener('scroll', () => {
  document.getElementById('nav').classList.toggle('scrolled', window.scrollY > 40);
}, { passive: true });

/* ── Scroll reveal ── */
const revealIO = new IntersectionObserver(entries => {
  entries.forEach(({ isIntersecting, target }) => {
    if (isIntersecting) { target.classList.add('visible'); revealIO.unobserve(target); }
  });
}, { threshold: 0.07 });

document.querySelectorAll('.reveal').forEach((el, i) => {
  el.style.transitionDelay = (i % 4) * 60 + 'ms';
  revealIO.observe(el);
});

/* ── 3D tilt on click/tap ── */
document.querySelectorAll('.gallery-item img, .screen-body img').forEach(img => {
  let busy = false;
  const box = img.closest('.gallery-item') || img.closest('.screen-frame') || img;

  function doTilt() {
    if (busy) return;
    busy = true;
    box.style.transition = 'transform 0.13s ease';
    box.style.transform = 'perspective(700px) rotateY(18deg) scale(0.95)';
    setTimeout(() => {
      box.style.transform = 'perspective(700px) rotateY(-14deg) scale(0.95)';
      setTimeout(() => {
        box.style.transition = 'transform 0.22s ease';
        box.style.transform = '';
        setTimeout(() => { busy = false; }, 220);
      }, 130);
    }, 130);
  }
  img.addEventListener('click', doTilt);
  img.addEventListener('touchend', e => { e.preventDefault(); doTilt(); }, { passive: false });
});

/* ── Smooth anchor scroll ── */
document.querySelectorAll('a[href^="#"]').forEach(a => {
  a.addEventListener('click', e => {
    const t = document.querySelector(a.getAttribute('href'));
    if (t) { e.preventDefault(); t.scrollIntoView({ behavior: 'smooth' }); }
  });
});
