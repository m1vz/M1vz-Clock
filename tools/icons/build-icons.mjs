import { readFileSync, writeFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const [lucideIcons, output] = process.argv.slice(2);
if (!lucideIcons || !output) {
  console.error("uso: node build-icons.mjs <pasta icons do lucide> <Icons.xaml>");
  process.exit(1);
}

const map = JSON.parse(readFileSync(join(here, "icons.json"), "utf8"));

const number = (value) => {
  const rounded = Math.round(value * 1000) / 1000;
  return Object.is(rounded, -0) ? "0" : String(rounded);
};

const point = (x, y) => `${number(x)},${number(y)}`;

function tokenize(d) {
  const tokens = [];
  let i = 0;
  while (i < d.length) {
    const c = d[i];
    if (/[MmLlHhVvCcSsQqTtAaZz]/.test(c)) {
      tokens.push({ command: c });
      i++;
    } else if (/[\s,]/.test(c)) {
      i++;
    } else {
      const match = /^[-+]?(\d*\.\d+|\d+\.?)([eE][-+]?\d+)?/.exec(d.slice(i));
      if (!match) {
        throw new Error(`caractere inesperado '${c}' em ${d}`);
      }
      tokens.push({ value: match[0] });
      i += match[0].length;
    }
  }
  return tokens;
}

function readFlag(tokens, state) {
  const token = tokens[state.index];
  if (!token || token.value === undefined) {
    throw new Error("arco sem flag");
  }
  if (token.value.length === 1) {
    state.index++;
    return token.value;
  }
  const flag = token.value[0];
  token.value = token.value.slice(1);
  return flag;
}

function readNumber(tokens, state) {
  const token = tokens[state.index++];
  if (!token || token.value === undefined) {
    throw new Error("número esperado");
  }
  return Number(token.value);
}

const arity = { M: 2, L: 2, H: 1, V: 1, C: 6, S: 4, Q: 4, T: 2, A: 7, Z: 0 };

function convertPath(d) {
  const tokens = tokenize(d);
  const state = { index: 0 };
  const parts = [];
  let command = null;
  while (state.index < tokens.length) {
    const token = tokens[state.index];
    if (token.command) {
      command = token.command;
      state.index++;
      if (command.toUpperCase() === "Z") {
        parts.push(command);
        continue;
      }
    } else if (command === null) {
      throw new Error(`caminho começa sem comando: ${d}`);
    }
    const upper = command.toUpperCase();
    if (upper === "A") {
      const rx = readNumber(tokens, state);
      const ry = readNumber(tokens, state);
      const rotation = readNumber(tokens, state);
      const large = readFlag(tokens, state);
      const sweep = readFlag(tokens, state);
      const x = readNumber(tokens, state);
      const y = readNumber(tokens, state);
      parts.push(`${command} ${point(rx, ry)} ${number(rotation)} ${large} ${sweep} ${point(x, y)}`);
    } else {
      const values = [];
      for (let n = 0; n < arity[upper]; n++) {
        values.push(readNumber(tokens, state));
      }
      const pairs = [];
      if (upper === "H" || upper === "V") {
        pairs.push(number(values[0]));
      } else {
        for (let n = 0; n < values.length; n += 2) {
          pairs.push(point(values[n], values[n + 1]));
        }
      }
      if (upper === "M" && command === "m" && parts.length === 0) {
        parts.push(`M ${pairs.join(" ")}`);
      } else {
        parts.push(`${command} ${pairs.join(" ")}`);
      }
      if (upper === "M") {
        command = command === "M" ? "L" : "l";
      }
    }
  }
  return parts.join(" ");
}

const attributes = (element) => {
  const result = {};
  for (const match of element.matchAll(/([a-zA-Z][a-zA-Z0-9-]*)="([^"]*)"/g)) {
    result[match[1]] = match[2];
  }
  return result;
};

function ellipse(cx, cy, rx, ry) {
  return `M ${point(cx - rx, cy)} A ${point(rx, ry)} 0 1 1 ${point(cx + rx, cy)} A ${point(rx, ry)} 0 1 1 ${point(cx - rx, cy)} Z`;
}

function rect(a) {
  const x = Number(a.x ?? 0);
  const y = Number(a.y ?? 0);
  const w = Number(a.width);
  const h = Number(a.height);
  const rx = Math.min(Number(a.rx ?? a.ry ?? 0), w / 2);
  const ry = Math.min(Number(a.ry ?? a.rx ?? 0), h / 2);
  if (!rx) {
    return `M ${point(x, y)} H ${number(x + w)} V ${number(y + h)} H ${number(x)} Z`;
  }
  return [
    `M ${point(x + rx, y)}`,
    `H ${number(x + w - rx)}`,
    `A ${point(rx, ry)} 0 0 1 ${point(x + w, y + ry)}`,
    `V ${number(y + h - ry)}`,
    `A ${point(rx, ry)} 0 0 1 ${point(x + w - rx, y + h)}`,
    `H ${number(x + rx)}`,
    `A ${point(rx, ry)} 0 0 1 ${point(x, y + h - ry)}`,
    `V ${number(y + ry)}`,
    `A ${point(rx, ry)} 0 0 1 ${point(x + rx, y)}`,
    "Z",
  ].join(" ");
}

function points(list, close) {
  const values = list.trim().split(/[\s,]+/).map(Number);
  const pairs = [];
  for (let n = 0; n < values.length; n += 2) {
    pairs.push(point(values[n], values[n + 1]));
  }
  return `M ${pairs[0]} ${pairs.slice(1).map((p) => `L ${p}`).join(" ")}${close ? " Z" : ""}`;
}

function convertSvg(svg) {
  const figures = [];
  for (const match of svg.matchAll(/<(path|circle|ellipse|rect|line|polyline|polygon)\b([^>]*)\/?>/g)) {
    const [, tag, body] = match;
    const a = attributes(body);
    switch (tag) {
      case "path":
        figures.push(convertPath(a.d));
        break;
      case "circle":
        figures.push(ellipse(Number(a.cx), Number(a.cy), Number(a.r), Number(a.r)));
        break;
      case "ellipse":
        figures.push(ellipse(Number(a.cx), Number(a.cy), Number(a.rx), Number(a.ry)));
        break;
      case "rect":
        figures.push(rect(a));
        break;
      case "line":
        figures.push(`M ${point(Number(a.x1), Number(a.y1))} L ${point(Number(a.x2), Number(a.y2))}`);
        break;
      case "polyline":
        figures.push(points(a.points, false));
        break;
      case "polygon":
        figures.push(points(a.points, true));
        break;
    }
  }
  return figures.join(" ");
}

const lines = [
  '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
  '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
];
for (const [key, name] of Object.entries(map)) {
  const svg = readFileSync(join(lucideIcons, `${name}.svg`), "utf8");
  const data = convertSvg(svg);
  if (!data || data.includes("NaN")) {
    throw new Error(`ícone ${name} gerou geometria inválida: ${data}`);
  }
  lines.push(`  <StreamGeometry x:Key="Icon.${key}">${data}</StreamGeometry>`);
}
lines.push("</ResourceDictionary>");
writeFileSync(output, lines.join("\r\n") + "\r\n", "utf8");
console.log(`${Object.keys(map).length} ícones gravados em ${output}`);
