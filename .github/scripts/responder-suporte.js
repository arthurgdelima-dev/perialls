// Responde sozinho a um pedido de suporte assim que ele é aberto (veja .github/workflows/responder-suporte.yml).
// Só lê o pedido e os arquivos de drivers/: não fecha, não marca e não muda nada no repositório.

const fs = require('fs');
const path = require('path');

// Aparelhos com driver escrito no código do app (BuiltInDevices e Compx em JsonDrivers/DriverCatalog.cs,
// Logitech em BuiltIn/LogitechHidpp.cs). Se mudar lá, mude aqui também.
const BUILT_IN_IDS = ['VID_3151&PID_502D'];
const BUILT_IN_VENDORS = { '373B': 'ATK/VXE/MAD R (Compx)', '3554': 'ATK/VXE/MAD R (Compx)', '046D': 'Logitech' };

// Acha os códigos USB no texto: "VID_3151&PID_502D" (Windows) ou "3151:502d" (lsusb).
function findIds(text) {
  const ids = new Set();
  for (const m of text.matchAll(/VID_([0-9a-f]{4})&PID_([0-9a-f]{4})/gi)) ids.add(`VID_${m[1]}&PID_${m[2]}`.toUpperCase());
  for (const m of text.matchAll(/\b([0-9a-f]{4}):([0-9a-f]{4})\b/gi)) ids.add(`VID_${m[1]}&PID_${m[2]}`.toUpperCase());
  return [...ids];
}

function readJson(dir, file) {
  try { return JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8')); } catch { return null; }
}

// O que já existe para estes códigos: driver JSON, driver embutido ou OpenRGB.
function lookup(ids, driversDir) {
  const index = readJson(driversDir, 'index.json') || { drivers: [] };
  const openrgb = new Set((readJson(driversDir, 'openrgb.json') || { devices: [] }).devices.map(d => d.toUpperCase()));
  for (const id of ids) {
    const entry = (index.drivers || []).find(e => (e.devices || []).map(d => d.toUpperCase()).includes(id));
    if (entry) return { kind: 'json', id, name: entry.name };
  }
  for (const id of ids) {
    if (BUILT_IN_IDS.includes(id)) return { kind: 'builtin', id };
    const vendor = BUILT_IN_VENDORS[id.slice(4, 8)];
    if (vendor) return { kind: 'builtin', id, name: vendor };
  }
  for (const id of ids) if (openrgb.has(id)) return { kind: 'openrgb', id };
  return { kind: 'none' };
}

const TEXT = {
  pt: {
    hello: 'Olá! Esta é uma resposta automática, e o mantenedor também vai ler o seu pedido. 👋',
    noId: 'Não encontrei o **código USB (VID/PID)** no pedido. Sem ele não dá para saber qual é o aparelho. ' +
      'O jeito mais fácil é abrir o pedido pelo ícone ➕ no cartão do aparelho, no Perialls, que já preenche tudo. ' +
      'Ou edite o pedido e cole o código (Windows: Gerenciador de Dispositivos → Propriedades → Detalhes → IDs de hardware; Linux: `lsusb`).',
    json: (r) => `Boa notícia: o aparelho \`${r.id}\` já tem driver (**${r.name}**). ` +
      'Abra o Perialls e clique no ícone ⬇ **Baixar driver** no cartão dele. Se o ícone não aparecer, atualize o app para a última versão.',
    builtin: (r) => `O aparelho \`${r.id}\` já tem suporte embutido no app${r.name ? ` (${r.name})` : ''}. ` +
      'Atualize o Perialls para a [última versão](https://github.com/arthurgdelima-dev/perialls/releases/latest). ' +
      'Se mesmo assim as funções não aparecerem, responda aqui contando o que acontece.',
    openrgb: (r) => `O aparelho \`${r.id}\` é suportado pelo OpenRGB, que já vem junto com o Perialls. ` +
      'Atualize o app para a [última versão](https://github.com/arthurgdelima-dev/perialls/releases/latest) e o controle de cor deve aparecer no cartão. ' +
      'No Linux, se o app avisar sobre permissão USB, siga o aviso dele. Se não funcionar, responda aqui.',
    none: (r) => 'Ainda não há driver para este aparelho. O pedido fica aberto para quem quiser ajudar.',
    dupes: (list) => `Já existe pedido para o mesmo aparelho: ${list}. Se puder, deixe um 👍 lá, assim fica mais fácil ver o que mais gente quer.`,
    help: 'Quer acelerar? Um driver é só um arquivo JSON, sem precisar saber programar. ' +
      'O guia [como adicionar um aparelho](https://github.com/arthurgdelima-dev/perialls/blob/main/docs/como-adicionar-um-aparelho.md) ' +
      'explica como descobrir os comandos com o software oficial da marca.',
    askSoftware: 'Se souber, conte aqui qual é o **software oficial** da marca (nome ou link). Isso ajuda muito a descobrir os comandos.',
  },
  en: {
    hello: 'Hi! This is an automatic reply, and the maintainer will read your request too. 👋',
    noId: 'I could not find the **USB code (VID/PID)** in the request, so there is no way to tell which device it is. ' +
      'The easiest way is to open the request from the ➕ icon on the device card in Perialls, which fills everything in. ' +
      'Or edit the request and paste the code (Windows: Device Manager → Properties → Details → Hardware IDs; Linux: `lsusb`).',
    json: (r) => `Good news: device \`${r.id}\` already has a driver (**${r.name}**). ` +
      'Open Perialls and click the ⬇ **Download driver** icon on its card. If the icon is missing, update the app to the latest version.',
    builtin: (r) => `Device \`${r.id}\` already has built-in support in the app${r.name ? ` (${r.name})` : ''}. ` +
      'Update Perialls to the [latest version](https://github.com/arthurgdelima-dev/perialls/releases/latest). ' +
      'If the controls still do not show up, reply here and tell us what happens.',
    openrgb: (r) => `Device \`${r.id}\` is supported by OpenRGB, which ships with Perialls. ` +
      'Update the app to the [latest version](https://github.com/arthurgdelima-dev/perialls/releases/latest) and the color control should show up on its card. ' +
      'On Linux, if the app warns about USB permission, follow its message. If it still does not work, reply here.',
    none: (r) => 'There is no driver for this device yet. The request stays open for anyone who wants to help.',
    dupes: (list) => `There is already a request for the same device: ${list}. Please leave a 👍 there, it helps show what people want most.`,
    help: 'Want to speed it up? A driver is just a JSON file, no programming needed. ' +
      'The guide [how to add a device](https://github.com/arthurgdelima-dev/perialls/blob/main/docs/como-adicionar-um-aparelho.md) ' +
      'explains how to find the commands using the brand\'s official software.',
    askSoftware: 'If you know it, tell us the brand\'s **official software** (name or link). It helps a lot to find the commands.',
  },
};

// Monta a resposta. "dupes" são outros pedidos abertos com o mesmo código ([{number}]).
function buildReply({ title, body, driversDir, dupes = [] }) {
  const lang = /^support for/i.test(title || '') ? 'en' : 'pt';
  const t = TEXT[lang];
  const text = `${title || ''}\n${body || ''}`;
  const ids = findIds(text);
  const parts = [t.hello];

  if (ids.length === 0) {
    parts.push(t.noId);
    return { lang, ids, result: { kind: 'noId' }, text: parts.join('\n\n') };
  }

  const result = lookup(ids, driversDir);
  parts.push(t[result.kind](result));
  if (result.kind === 'none') {
    if (dupes.length) parts.push(t.dupes(dupes.map(d => `#${d.number}`).join(', ')));
    parts.push(t.help);
    // No formulário, campo vazio aparece como "_No response_".
    const software = (body || '').match(/### (Software oficial|Official software)\s+([\s\S]*?)(\n###|$)/i);
    if (!software || /^_no response_$/i.test(software[2].trim()) || !software[2].trim()) parts.push(t.askSoftware);
  }
  return { lang, ids, result, text: parts.join('\n\n') };
}

// Chamado pelo actions/github-script.
async function run({ github, context, core }) {
  const issue = context.payload.issue;
  const { owner, repo } = context.repo;
  const ids = findIds(`${issue.title}\n${issue.body || ''}`);

  // Outros pedidos abertos que citam o mesmo código.
  const dupes = [];
  for (const id of ids) {
    const q = `repo:${owner}/${repo} is:issue is:open "${id}"`;
    const { data } = await github.rest.search.issuesAndPullRequests({ q, per_page: 10 });
    for (const item of data.items) {
      if (item.number !== issue.number && !dupes.some(d => d.number === item.number)) dupes.push({ number: item.number });
    }
  }

  const reply = buildReply({ title: issue.title, body: issue.body, driversDir: path.join(process.env.GITHUB_WORKSPACE, 'drivers'), dupes });
  core.info(`Códigos: ${reply.ids.join(', ') || '(nenhum)'} → ${reply.result.kind}`);
  await github.rest.issues.createComment({ owner, repo, issue_number: issue.number, body: reply.text });
}

module.exports = { run, buildReply, findIds, lookup };
