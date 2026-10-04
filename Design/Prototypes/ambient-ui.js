"use strict";

const states = {
  idle: { color: "#89939e", label: "Hidden", title: "", body: "", actions: [] },
  listening: { color: "#6ae1da", label: "Listening", title: "I'm listening.", body: "Say “Kora” to start in the desktop design. This prototype uses the Wake Kora button or optional hold-Space push-to-talk to simulate capture.", actions: [["Finish utterance", "calculate"], ["Discard", "cancel"]] },
  calculating: { color: "#af9bff", label: "Thinking", title: "Making sense of it.", body: "Preparing an explanation of the selected text. Nothing is being changed.", actions: [["Cancel task", "cancel"]] },
  waiting: { color: "#f0cc83", label: "Needs your approval", title: "Send this text to Copilot?", body: "A remote explanation needs your permission. Review the selected content and destination before continuing.", actions: [["Approve once", "approve"], ["Keep local", "deny"]] },
  executing: { color: "#80b7ff", label: "Working", title: "Explaining the selected text.", body: "The approved request is running. Any partial answer remains incomplete until the task finishes.", actions: [["Cancel task", "cancel"]] },
  success: { color: "#9bdfac", label: "Complete", title: "A little more clarity.", body: "The exception indicates a missing configuration value. Check the named setting before retrying. This is an illustrative answer, not a diagnosis.", actions: [["Keep visible", "pin"], ["Dismiss", "dismiss"]] },
  failure: { color: "#f49c9c", label: "Couldn't finish", title: "The runtime disconnected.", body: "No complete answer is available. Partial output is incomplete. Kora has not switched providers or retried automatically.", actions: [["Retry read-only request", "retry"], ["Dismiss", "dismiss"]] },
  information: { color: "#b8d9ec", label: "Information", title: "Here's the short version.", body: "Kora can explain text you explicitly select. The clipboard is never monitored. You choose what to share and where it is processed.", actions: [["Dismiss", "dismiss"]] }
};
const conceptNotes = {
  swarm: ["Individual points wander, dart, and change course like insects in a loose collective. No shared orbit, no rigid sphere.", "PARTICLE CLOUD / WANDER + DART"],
  orbit: ["A few fine ribbons describe an open sphere. Calmer and more minimal, with less particle shimmer.", "RIBBON ORBIT / FLOW + SETTLE"],
  lattice: ["A sparse three-dimensional lattice. Deliberate and architectural, without the visual noise of a busy wireframe.", "SIGNAL LATTICE / ROTATE + ALIGN"]
};
const $ = (id) => document.getElementById(id);
const canvas = $("particles");
const ctx = canvas.getContext("2d");
if (!ctx) throw new Error("Kora's animation preview requires a browser with Canvas 2D support.");
const motionPreference = window.matchMedia("(prefers-reduced-motion: reduce)");
let state = "calculating";
let concept = "swarm";
let intensity = 1;
let reduced = motionPreference.matches;
let remoteApproved = false;
let pinned = false;
let spaceHeld = false;
let touring = false;
let timer = null;
let tourTimer = null;
let frame = null;
let phase = 0;
let lastFrame = 0;
let color = hexRgb(states[state].color);
let targetColor = [...color];

$("reduce").checked = reduced;
$("system-motion").textContent = reduced ? "System preference: reduced motion." : "";
document.body.classList.toggle("reduce-motion", reduced);
$("response").setAttribute("aria-live", "polite");
$("response").setAttribute("aria-atomic", "true");
$("state-label").setAttribute("aria-live", "polite");

function hexRgb(hex) {
  return [1, 3, 5].map((offset) => parseInt(hex.slice(offset, offset + 2), 16));
}

function clearTaskTimer() {
  window.clearTimeout(timer);
  timer = null;
}

function stopTour() {
  touring = false;
  window.clearTimeout(tourTimer);
  tourTimer = null;
  $("tour").textContent = "Play sequence \u25b6";
}

function setState(next, options = {}) {
  clearTaskTimer();
  if (!options.tour) stopTour();
  state = next;
  if (next === "idle" || next === "listening") remoteApproved = false;
  pinned = false;
  const data = states[next];
  targetColor = hexRgb(data.color);
  if (reduced) color = [...targetColor];
  $("presence").dataset.state = next;
  $("presence").style.setProperty("--state-color", data.color);
  $("presence").classList.toggle("is-hidden", next === "idle");
  $("presence").inert = next === "idle";
  $("idle-hint").hidden = next !== "idle";
  $("state-label").textContent = data.label;
  const remote = next === "waiting" || remoteApproved;
  $("runtime").textContent = remote ? "COPILOT / REMOTE / SIMULATED" : "LOCAL ONLY / SIMULATED";
  $("response-title").textContent = data.title;
  $("response-body").textContent = data.body;
  $("approval-scope").hidden = next !== "waiting";
  // Decisions and errors retain their explanation even in compact mode.
  $("response").hidden = next === "idle" || (!$("details").checked && !["waiting", "failure"].includes(next));
  $("visual").setAttribute("aria-label", `Kora: ${data.label}. Show or hide response details.`);
  $("visual").setAttribute("aria-expanded", String(!$("response").hidden));
  const closeLabel = ["listening", "calculating", "waiting", "executing"].includes(next) ? "Cancel task" : "Dismiss response and hide Kora";
  $("dismiss").setAttribute("aria-label", closeLabel);
  $("dismiss").title = closeLabel;
  $("response-actions").replaceChildren();
  for (const [label, action] of data.actions) {
    const button = document.createElement("button");
    button.textContent = label;
    if (action === "approve") button.className = "primary";
    button.addEventListener("click", () => performAction(action, button));
    $("response-actions").append(button);
  }
  document.querySelectorAll("[data-state]").forEach((button) => {
    if (!(button instanceof HTMLButtonElement)) return;
    const selected = button.dataset.state === next;
    button.classList.toggle("selected", selected);
    button.setAttribute("aria-pressed", String(selected));
  });
  if (next === "success" && !options.tour) {
    timer = window.setTimeout(() => {
      if (!pinned && !$("presence").matches(":hover") && !$("presence").contains(document.activeElement)) setState("idle");
    }, 6500);
  }
  drawOnce();
  scheduleFrame();
}

function restoreFocus() {
  $("wake").focus({ preventScroll: true });
}

function performAction(action, button) {
  stopTour();
  switch (action) {
    case "approve":
      remoteApproved = true;
      setState("executing");
      restoreFocus();
      timer = window.setTimeout(() => setState("success"), 3200);
      break;
    case "deny":
      remoteApproved = false;
      setState("information");
      $("response-title").textContent = "Nothing was sent.";
      $("response-body").textContent = "The remote action was cancelled. A compatible local runtime would be needed to continue locally.";
      restoreFocus();
      break;
    case "cancel":
      spaceHeld = false;
      setState("information");
      $("response-title").textContent = "Cancelled.";
      $("response-body").textContent = remoteApproved
        ? "Cancellation was requested. It cannot prove a remote operation stopped; late completion may still occur. No retry was started."
        : "The simulated capture or task was discarded. No new work will start.";
      restoreFocus();
      break;
    case "calculate":
      spaceHeld = false;
      finishUtterance();
      restoreFocus();
      break;
    case "retry":
      setState("calculating");
      restoreFocus();
      timer = window.setTimeout(() => setState("waiting"), 2200);
      break;
    case "pin":
      pinned = true;
      clearTaskTimer();
      button.textContent = "Kept visible";
      button.disabled = true;
      break;
    case "dismiss":
      spaceHeld = false;
      setState("idle");
      restoreFocus();
      break;
    default:
      throw new Error(`Unknown prototype action: ${action}`);
  }
}

function finishUtterance() {
  setState("calculating");
  timer = window.setTimeout(() => setState("waiting"), 2200);
}

function endHold() {
  if (!spaceHeld) return;
  spaceHeld = false;
  if (state === "listening") finishUtterance();
}

document.querySelectorAll("button[data-state]").forEach((button) => {
  button.addEventListener("click", () => {
    spaceHeld = false;
    remoteApproved = false;
    setState(button.dataset.state);
  });
});
document.querySelectorAll("[data-concept]").forEach((button) => {
  button.addEventListener("click", () => {
    concept = button.dataset.concept;
    document.querySelectorAll("[data-concept]").forEach((item) => {
      const selected = item === button;
      item.classList.toggle("selected", selected);
      item.setAttribute("aria-pressed", String(selected));
    });
    $("concept-note").textContent = conceptNotes[concept][0];
    $("motion-name").textContent = conceptNotes[concept][1];
    drawOnce();
    scheduleFrame();
  });
});
$("light").addEventListener("change", (event) => {
  $("desktop").classList.toggle("light", event.target.checked);
  $("preview-mode").textContent = event.target.checked ? "LIGHT DESKTOP" : "DARK DESKTOP";
  drawOnce();
});
function updateReducedMotion(value) {
  reduced = value;
  lastFrame = 0;
  $("reduce").checked = value;
  document.body.classList.toggle("reduce-motion", value);
  color = [...targetColor];
  if (frame !== null) cancelAnimationFrame(frame);
  frame = null;
  drawOnce();
  scheduleFrame();
}
$("reduce").addEventListener("change", (event) => updateReducedMotion(event.target.checked));
motionPreference.addEventListener("change", (event) => updateReducedMotion(event.matches));
$("intensity").addEventListener("input", (event) => {
  intensity = Number(event.target.value);
  $("intensity-label").textContent = ["Quiet", "Balanced", "Expressive"][intensity];
  drawOnce();
});
$("details").addEventListener("change", () => {
  $("response").hidden = state === "idle" || (!$("details").checked && !["waiting", "failure"].includes(state));
  $("visual").setAttribute("aria-expanded", String(!$("response").hidden));
});
$("visual").addEventListener("click", () => {
  if (["waiting", "failure"].includes(state)) return;
  $("details").checked = !$("details").checked;
  $("details").dispatchEvent(new Event("change"));
});
$("wake").addEventListener("click", () => {
  spaceHeld = false;
  remoteApproved = false;
  $("details").checked = true;
  setState("information");
});
$("dismiss").addEventListener("click", () => {
  performAction(["listening", "calculating", "waiting", "executing"].includes(state) ? "cancel" : "dismiss");
});
$("tour").addEventListener("click", () => {
  if (touring) {
    stopTour();
    return;
  }
  clearTaskTimer();
  spaceHeld = false;
  remoteApproved = false;
  touring = true;
  $("tour").textContent = "Stop sequence \u25a0";
  const sequence = ["idle", "listening", "calculating", "waiting", "executing", "success", "information", "failure"];
  let index = 0;
  function advance() {
    if (!touring) return;
    const next = sequence[index++];
    // The tour demonstrates visuals; it does not grant permission or invoke an action.
    setState(next, { tour: true });
    if (index < sequence.length) tourTimer = window.setTimeout(advance, next === "idle" ? 1800 : 3000);
    else stopTour();
  }
  advance();
});
document.addEventListener("keydown", (event) => {
  if (event.code === "Escape") {
    event.preventDefault();
    if (state !== "idle") performAction(["listening", "calculating", "waiting", "executing"].includes(state) ? "cancel" : "dismiss");
    else stopTour();
    return;
  }
  if (event.code !== "Space" || event.repeat || event.altKey || event.ctrlKey || event.metaKey) return;
  const target = event.target;
  if (target instanceof Element && target.closest("button, input, select, textarea, a, [contenteditable]")) return;
  event.preventDefault();
  if (!["idle", "information", "success", "failure"].includes(state)) return;
  spaceHeld = true;
  setState("listening");
});
document.addEventListener("keyup", (event) => {
  if (event.code === "Space" && spaceHeld) {
    event.preventDefault();
    endHold();
  }
});
window.addEventListener("blur", () => {
  if (spaceHeld) {
    spaceHeld = false;
    performAction("cancel");
  }
});
document.addEventListener("visibilitychange", () => {
  if (document.hidden) {
    if (frame !== null) cancelAnimationFrame(frame);
    frame = null;
    if (spaceHeld) {
      spaceHeld = false;
      performAction("cancel");
    }
    stopTour();
  } else {
    lastFrame = 0;
    drawOnce();
    scheduleFrame();
  }
});

function particleRandom(seed) {
  return () => {
    seed ^= seed << 13;
    seed ^= seed >>> 17;
    seed ^= seed << 5;
    return (seed >>> 0) / 4294967296;
  };
}
function randomDirection(random) {
  const z = random() * 2 - 1;
  const angle = random() * Math.PI * 2;
  const radius = Math.sqrt(1 - z * z);
  return [Math.cos(angle) * radius, Math.sin(angle) * radius, z];
}
const swarm = Array.from({ length: 180 }, (_, index) => {
  const random = particleRandom(Math.imul(index + 1, 2654435761));
  const radius = Math.cbrt(random()) * .9;
  const direction = randomDirection(random);
  return {
    position: randomDirection(random).map((value) => value * radius),
    velocity: direction.map((value) => value * .3),
    direction,
    random,
    turnIn: .2 + random(),
    speed: .65 + random() * .45,
    dart: 0,
    size: .8 + random() * .4
  };
});
const swarmActivity = { listening: .8, calculating: 1, waiting: .16, executing: 1.3, success: .12, failure: 0, information: .35 };
let swarmTime = 0;

function stepSwarm(delta) {
  const forces = swarm.map((particle, index) => {
    particle.turnIn -= delta;
    if (particle.turnIn <= 0) {
      particle.direction = randomDirection(particle.random);
      particle.turnIn = .35 + particle.random() * 1.1;
      particle.dart = particle.random() < .2 ? .8 : 0;
    }
    particle.dart *= Math.exp(-delta * 3);
    const radius = Math.hypot(...particle.position);
    const pull = .7 + Math.max(0, radius - .6) * 7;
    const force = particle.direction.map((value, axis) =>
      value * (1.8 + particle.dart) * particle.speed - particle.position[axis] * pull - particle.velocity[axis] * 1.4);
    // Local separation and a soft centre pull keep independent paths in a loose collective.
    for (let other = 0; other < swarm.length; other++) {
      if (other === index) continue;
      const neighbour = swarm[other].position;
      const dx = particle.position[0] - neighbour[0];
      const dy = particle.position[1] - neighbour[1];
      const dz = particle.position[2] - neighbour[2];
      const distanceSquared = dx * dx + dy * dy + dz * dz;
      if (distanceSquared < .0484) {
        const strength = .022 * (1 - distanceSquared / .0484) / (distanceSquared + .008);
        force[0] += dx * strength;
        force[1] += dy * strength;
        force[2] += dz * strength;
      }
    }
    return force;
  });
  swarm.forEach((particle, index) => {
    for (let axis = 0; axis < 3; axis++) particle.velocity[axis] += forces[index][axis] * delta;
    const speed = Math.hypot(...particle.velocity);
    const limit = particle.speed * (1 + particle.dart);
    if (speed > limit) particle.velocity = particle.velocity.map((value) => value * limit / speed);
    for (let axis = 0; axis < 3; axis++) particle.position[axis] += particle.velocity[axis] * delta;
    const radius = Math.hypot(...particle.position);
    if (radius > 1.05) {
      const normal = particle.position.map((value) => value / radius);
      particle.position = normal.map((value) => value * 1.05);
      const outward = particle.velocity.reduce((sum, value, axis) => sum + value * normal[axis], 0);
      if (outward > 0) particle.velocity = particle.velocity.map((value, axis) => value - normal[axis] * outward * 1.5);
    }
  });
}
function updateSwarm(delta) {
  if (reduced || concept !== "swarm" || state === "idle") return;
  swarmTime += delta * swarmActivity[state];
  // Fixed simulation steps avoid frame-rate-dependent jitter or bursts after a pause.
  const step = 1 / 60;
  while (swarmTime >= step) {
    stepSwarm(step);
    swarmTime -= step;
  }
}
const latticeVertices = Array.from({ length: 27 }, (_, i) => [(i % 3) - 1, (Math.floor(i / 3) % 3) - 1, Math.floor(i / 9) - 1]);
const latticeEdges = [];
for (let i = 0; i < latticeVertices.length; i++) {
  for (let j = i + 1; j < latticeVertices.length; j++) {
    const distance = latticeVertices[i].reduce((sum, value, axis) => sum + Math.abs(value - latticeVertices[j][axis]), 0);
    if (distance === 1) latticeEdges.push([i, j]);
  }
}

function project(point, angle, scale, tilt = .35) {
  const [x, y, z] = point;
  const rx = x * Math.cos(angle) + z * Math.sin(angle);
  const rz = z * Math.cos(angle) - x * Math.sin(angle);
  const ry = y * Math.cos(tilt) - rz * Math.sin(tilt);
  const depth = y * Math.sin(tilt) + rz * Math.cos(tilt);
  const perspective = 3.8 / (3.8 - depth);
  return [220 + rx * scale * perspective, 155 + ry * scale * perspective, depth];
}
function ink(alpha) {
  // Dark pigment rather than glow preserves the transparent visual on pale desktops.
  const rgb = $("light").checked ? color.map((value) => Math.round(value * .53)) : color.map(Math.round);
  return `rgba(${rgb.join(",")},${Math.max(0, Math.min(1, alpha))})`;
}
function line(a, b, alpha, width = .7) {
  ctx.beginPath();
  ctx.moveTo(a[0], a[1]);
  ctx.lineTo(b[0], b[1]);
  ctx.strokeStyle = ink(alpha);
  ctx.lineWidth = width;
  ctx.stroke();
}
function dot(point, radius, alpha) {
  ctx.beginPath();
  ctx.arc(point[0], point[1], radius, 0, Math.PI * 2);
  ctx.fillStyle = ink(alpha);
  ctx.fill();
}
function drawOnce() {
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  if (state === "idle") return;
  const moving = !reduced;
  const t = moving ? phase : 0;
  const activity = { listening: .75, calculating: 1, waiting: .14, executing: 1.2, success: .18, failure: 0, information: .25 }[state];
  const angle = state === "failure" ? .35 : state === "success" ? .7 + t * .045 : t * activity * .23;
  const breathe = moving ? Math.sin(t * (state === "listening" ? 2.1 : .8)) : 0;
  const baseScale = [51, 61, 70][intensity];
  const scale = concept === "swarm" ? baseScale : baseScale * (1 + breathe * (state === "listening" ? .10 : .025));
  const alpha = [.65, .85, 1][intensity];
  if (!$("light").checked) {
    const glow = ctx.createRadialGradient(220, 155, 0, 220, 155, scale * 1.6);
    glow.addColorStop(0, ink(.075 * alpha));
    glow.addColorStop(1, ink(0));
    ctx.fillStyle = glow;
    ctx.fillRect(75, 15, 290, 280);
  }
  if (concept === "swarm") {
    const count = [96, 140, 180][intensity];
    const projected = swarm.slice(0, count).map((particle) => ({
      point: project(particle.position, 0, scale),
      size: particle.size
    }));
    projected.sort((a, b) => a.point[2] - b.point[2]).forEach(({ point, size }) => {
      const depth = (point[2] + 1.2) / 2.4;
      dot(point, (1.15 + depth * 1.8) * size, (.4 + depth * .55) * alpha);
    });
  } else if (concept === "orbit") {
    for (let ring = 0; ring < 4; ring++) {
      let previous = null;
      for (let i = 0; i <= 160; i++) {
        const a = i / 160 * Math.PI * 2;
        const offset = ring * Math.PI / 4;
        const ripple = 1 + Math.sin(a * 3 + t * .7 + ring) * (state === "listening" ? .11 : .025);
        const point = project([Math.cos(a) * ripple, Math.sin(a) * Math.cos(offset) * ripple, Math.sin(a) * Math.sin(offset) * ripple], angle, scale);
        if (previous) line(previous, point, (.2 + (point[2] + 1) * .22) * alpha, 1.2);
        previous = point;
      }
      const a = t * activity * .45 + ring * 1.6;
      const point = project([Math.cos(a), Math.sin(a) * Math.cos(ring * Math.PI / 4), Math.sin(a) * Math.sin(ring * Math.PI / 4)], angle, scale);
      dot(point, 2.5, .95 * alpha);
    }
  } else {
    const projected = latticeVertices.map((point, i) => {
      const expand = state === "calculating" ? 1 + Math.sin(t * .9 + i * .5) * .045 : 1;
      return project(point.map((value) => value * expand * .65), angle, scale, .45);
    });
    for (const [i, j] of latticeEdges) line(projected[i], projected[j], (.16 + (projected[i][2] + 1) * .12) * alpha, .8);
    projected.forEach((point, i) => {
      const scan = moving && state === "executing" ? .45 + .5 * Math.max(0, Math.sin(t * 1.8 - i * .35)) : .65;
      dot(point, 1.6 + (point[2] + 1) * .6, scan * alpha);
    });
  }
}
function animate(now) {
  frame = null;
  if (document.hidden || state === "idle" || reduced) return;
  if (!lastFrame) lastFrame = now;
  const delta = Math.min((now - lastFrame) / 1000, .1);
  lastFrame = now;
  phase += delta;
  updateSwarm(delta);
  const blend = 1 - Math.exp(-delta * 5);
  color = color.map((value, i) => value + (targetColor[i] - value) * blend);
  drawOnce();
  scheduleFrame();
}
function scheduleFrame() {
  if (frame !== null || reduced || state === "idle" || document.hidden) return;
  frame = requestAnimationFrame(animate);
}

setState("calculating");
