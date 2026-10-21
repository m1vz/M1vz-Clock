import { readFileSync, writeFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const folder = join(here, "..", "..", "src", "M1vClock.App", "Localization");
const targets = [
  { file: join(folder, "Strings.resx"), column: 1 },
  { file: join(folder, "Strings.en.resx"), column: 2 },
];

const input = process.argv[2];
if (!input) {
  console.error("uso: node add-strings.mjs <arquivo.tsv com: chave<TAB>português<TAB>inglês>");
  process.exit(1);
}

const escape = (text) => text.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
const entries = readFileSync(input, "utf8")
  .replace(/\r\n/g, "\n")
  .split("\n")
  .filter((line) => line.trim().length > 0)
  .map((line) => line.split("\t"));

for (const parts of entries) {
  if (parts.length !== 3 || parts.some((p) => p.trim().length === 0)) {
    console.error(`linha inválida (esperado chave, português e inglês): ${parts.join(" | ")}`);
    process.exit(1);
  }
}

for (const { file, column } of targets) {
  let text = readFileSync(file, "utf8").replace(/\r\n/g, "\n");
  for (const parts of entries) {
    const key = parts[0];
    if (text.includes(`<data name="${key}"`)) {
      console.error(`a chave ${key} já existe em ${file}`);
      process.exit(1);
    }
  }

  const block = entries
    .map((parts) => `  <data name="${parts[0]}" xml:space="preserve">\n    <value>${escape(parts[column])}</value>\n  </data>\n`)
    .join("");
  text = text.replace(/<\/root>\s*$/, `${block}</root>\n`);
  writeFileSync(file, text, "utf8");
}

console.log(`${entries.length} textos adicionados em português e inglês.`);
