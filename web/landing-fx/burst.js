// The gold burst of a click (Three.js): small faceted pieces and thin coins thrown from the point of the click, turning
// in the light and falling away, sparks, and a ring of light that opens and fades. One clear canvas over the page,
// drawn only while something moves; made once the page has settled (fx.js).
import {
  AdditiveBlending, BufferAttribute, BufferGeometry, CanvasTexture, CylinderGeometry, DirectionalLight, DynamicDrawUsage,
  InstancedMesh, Mesh, MeshBasicMaterial, MeshStandardMaterial, Object3D, OctahedronGeometry, OrthographicCamera,
  PMREMGenerator, Points, PointsMaterial, RingGeometry, Scene, WebGLRenderer,
} from "three";
import { RoomEnvironment } from "three/examples/jsm/environments/RoomEnvironment.js";

const GRAVITY = 1500; // px per second², down the screen
const GOLD = [0.85, 0.66, 0.33];
const rand = (a, b) => a + Math.random() * (b - a);

export function makeBurst() {
  const canvas = document.createElement("canvas");
  canvas.className = "fx-layer";
  canvas.setAttribute("aria-hidden", "true");
  document.body.append(canvas);

  const renderer = new WebGLRenderer({ canvas, alpha: true, antialias: true, powerPreference: "low-power" });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.setClearColor(0x000000, 0);
  const scene = new Scene();
  const pmrem = new PMREMGenerator(renderer);
  scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture; // what the gold reflects
  pmrem.dispose();
  const sun = new DirectionalLight(0xffe2a8, 2.8);
  sun.position.set(-0.4, 1, 1);
  scene.add(sun);

  // the camera sees the window in pixels: x from the left, y down (as -y)
  const camera = new OrthographicCamera(0, 1, 0, -1, -2000, 2000);
  const fit = () => {
    renderer.setSize(window.innerWidth, window.innerHeight, false);
    camera.right = window.innerWidth;
    camera.bottom = -window.innerHeight;
    camera.updateProjectionMatrix();
  };
  fit();
  window.addEventListener("resize", fit);

  const solids = [
    // gold: a warm metal that keeps its colour in its own shadow too (the glow of emissive)
    pool(new OctahedronGeometry(1, 0), new MeshStandardMaterial({ color: 0xf2c25a, metalness: 0.9, roughness: 0.2, emissive: 0x5a3a0a, emissiveIntensity: 0.6, envMapIntensity: 0.55 }), 120),
    pool(new CylinderGeometry(1, 1, 0.24, 28), new MeshStandardMaterial({ color: 0xdba543, metalness: 0.9, roughness: 0.26, emissive: 0x4a2f08, emissiveIntensity: 0.55, envMapIntensity: 0.55 }), 80),
  ];
  for (const s of solids) scene.add(s.mesh);
  const sparks = sparkPool(220);
  scene.add(sparks.points);
  const rings = Array.from({ length: 5 }, () => {
    const m = new Mesh(new RingGeometry(0.86, 1, 72), new MeshBasicMaterial({ color: 0xf3d08a, transparent: true, opacity: 0, blending: AdditiveBlending, depthWrite: false }));
    m.visible = false;
    scene.add(m);
    return { mesh: m, age: 0, life: 0, size: 0, alive: false };
  });

  renderer.compile(scene, camera); // ready before the first click, so it starts without a stall

  let running = false;
  let last = 0;
  const tick = (t) => {
    const dt = Math.min(0.033, (t - last) / 1000);
    last = t;
    let alive = 0;
    for (const s of solids) alive += stepSolids(s, dt);
    alive += stepSparks(sparks, dt);
    for (const r of rings) alive += stepRing(r, dt);
    renderer.render(scene, camera);
    if (alive) requestAnimationFrame(tick);
    else running = false;
  };

  function fire(x, y, power = 1) {
    spawnSolids(solids[0], x, y, Math.round(26 * power), [5.5, 11]);
    spawnSolids(solids[1], x, y, Math.round(14 * power), [6.5, 10.5]);
    spawnSparks(sparks, x, y, Math.round(48 * power));
    const ring = rings.find((r) => !r.alive) ?? rings[0];
    Object.assign(ring, { alive: true, age: 0, life: 0.6, size: 48 + 46 * power });
    ring.mesh.position.set(x, -y, 10);
    ring.mesh.visible = true;
    if (!running) {
      running = true;
      last = performance.now();
      requestAnimationFrame(tick);
    }
  }
  return { fire };
}

// ---------- the pieces of gold ----------
const tmp = new Object3D();

function pool(geometry, material, n) {
  const mesh = new InstancedMesh(geometry, material, n);
  mesh.instanceMatrix.setUsage(DynamicDrawUsage);
  mesh.frustumCulled = false;
  const parts = Array.from({ length: n }, () => ({ alive: false }));
  tmp.scale.setScalar(0);
  tmp.updateMatrix();
  for (let i = 0; i < n; i++) mesh.setMatrixAt(i, tmp.matrix);
  return { mesh, parts, next: 0 };
}

function spawnSolids(p, x, y, count, [s0, s1]) {
  for (let k = 0; k < count; k++) {
    const part = p.parts[p.next];
    p.next = (p.next + 1) % p.parts.length;
    const a = rand(0, Math.PI * 2);
    const speed = rand(240, 640);
    Object.assign(part, {
      alive: true, age: 0, life: rand(0.9, 1.5), x, y, z: rand(-200, 200),
      vx: Math.cos(a) * speed, vy: Math.sin(a) * speed * 0.8 - rand(260, 520),
      rx: rand(0, 6.3), ry: rand(0, 6.3), rz: rand(0, 6.3), wx: rand(-9, 9), wy: rand(-9, 9), wz: rand(-6, 6),
      s: rand(s0, s1),
    });
  }
}

function stepSolids(p, dt) {
  let alive = 0;
  const drag = Math.exp(-1.4 * dt);
  p.parts.forEach((q, i) => {
    if (!q.alive) return;
    q.age += dt;
    if (q.age >= q.life) {
      q.alive = false;
      tmp.scale.setScalar(0);
    } else {
      alive++;
      q.vx *= drag;
      q.vy = q.vy * drag + GRAVITY * dt;
      q.x += q.vx * dt;
      q.y += q.vy * dt;
      q.rx += q.wx * dt; q.ry += q.wy * dt; q.rz += q.wz * dt;
      const fade = Math.min(1, (q.life - q.age) / (0.35 * q.life), q.age / 0.06); // grows in, shrinks away
      tmp.position.set(q.x, -q.y, q.z);
      tmp.rotation.set(q.rx, q.ry, q.rz);
      tmp.scale.setScalar(q.s * fade);
    }
    tmp.updateMatrix();
    p.mesh.setMatrixAt(i, tmp.matrix);
  });
  p.mesh.instanceMatrix.needsUpdate = true;
  return alive;
}

// ---------- the sparks ----------
function sparkPool(n) {
  const geometry = new BufferGeometry();
  const pos = new Float32Array(n * 3);
  const col = new Float32Array(n * 4); // with alpha: a fading spark leaves nothing on the clear canvas
  geometry.setAttribute("position", new BufferAttribute(pos, 3).setUsage(DynamicDrawUsage));
  geometry.setAttribute("color", new BufferAttribute(col, 4).setUsage(DynamicDrawUsage));
  const material = new PointsMaterial({
    size: 16, map: glow(), vertexColors: true, transparent: true, depthWrite: false, blending: AdditiveBlending, sizeAttenuation: false,
  });
  const points = new Points(geometry, material);
  points.frustumCulled = false;
  return { points, pos, col, parts: Array.from({ length: n }, () => ({ alive: false })), next: 0 };
}

function spawnSparks(p, x, y, count) {
  for (let k = 0; k < count; k++) {
    const part = p.parts[p.next];
    p.next = (p.next + 1) % p.parts.length;
    const a = rand(0, Math.PI * 2);
    const speed = rand(160, 760);
    Object.assign(part, { alive: true, age: 0, life: rand(0.35, 0.8), x, y, vx: Math.cos(a) * speed, vy: Math.sin(a) * speed - rand(80, 240) });
  }
}

function stepSparks(p, dt) {
  let alive = 0;
  const drag = Math.exp(-3.2 * dt);
  p.parts.forEach((q, i) => {
    let k = 0;
    if (q.alive) {
      q.age += dt;
      if (q.age >= q.life) q.alive = false;
      else {
        alive++;
        q.vx *= drag;
        q.vy = q.vy * drag + GRAVITY * 0.35 * dt;
        q.x += q.vx * dt;
        q.y += q.vy * dt;
        k = 1 - q.age / q.life;
      }
    }
    if (k > 0) p.pos.set([q.x, -q.y, 300], i * 3);
    else p.pos.set([-1e5, 0, 0], i * 3); // gone: out of sight
    p.col.set([GOLD[0] * 1.3, GOLD[1] * 1.3, GOLD[2] * 1.3, k], i * 4);
  });
  p.points.geometry.attributes.position.needsUpdate = true;
  p.points.geometry.attributes.color.needsUpdate = true;
  return alive;
}

function glow() {
  const c = document.createElement("canvas");
  c.width = c.height = 64;
  const g = c.getContext("2d");
  const r = g.createRadialGradient(32, 32, 0, 32, 32, 32);
  r.addColorStop(0, "rgba(255,255,255,1)");
  r.addColorStop(0.25, "rgba(255,240,200,0.85)");
  r.addColorStop(1, "rgba(255,220,150,0)");
  g.fillStyle = r;
  g.fillRect(0, 0, 64, 64);
  return new CanvasTexture(c);
}

// ---------- the ring of light ----------
function stepRing(r, dt) {
  if (!r.alive) return 0;
  r.age += dt;
  const t = r.age / r.life;
  if (t >= 1) {
    r.alive = false;
    r.mesh.visible = false;
    return 0;
  }
  const ease = 1 - (1 - t) ** 3;
  r.mesh.scale.setScalar(6 + r.size * ease);
  r.mesh.material.opacity = 0.85 * (1 - t);
  return 1;
}
