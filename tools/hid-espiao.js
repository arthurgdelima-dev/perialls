// Cole no Console do Chrome (F12) com o driver web da MonsGeek aberto.
// Só OBSERVA: anota os pacotes que o site manda e recebe do teclado e repassa tudo sem mudar nada.
// Não envia nenhum comando por conta própria.
//
//   marca('cor-vermelho')  -> dá nome para os próximos pacotes
//   salvar()               -> baixa um .json só com os pacotes da ação atual
//   salvarTudo()           -> baixa um .json com tudo que foi anotado
(() => {
    if (window.__hidLog) { console.log('O espião já está ativo.'); return; }

    const log = window.__hidLog = [];
    let acao = 'sem-nome';

    const hex = (data) => {
        const bytes = data instanceof ArrayBuffer
            ? new Uint8Array(data)
            : new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
        return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join(' ');
    };

    const anota = (tipo, device, reportId, data) => {
        const item = {
            hora: new Date().toISOString(),
            acao,
            tipo,
            pid: device.productId.toString(16).padStart(4, '0'),
            usagePage: device.collections.map(c => c.usagePage.toString(16)).join(','),
            reportId,
            bytes: hex(data),
        };
        log.push(item);
        const cor = tipo.startsWith('ENVIA') ? 'color:#e76f51' : 'color:#4ea8de';
        console.log(`%c[${acao}] ${tipo} id=${reportId} ${item.bytes}`, cor);
    };

    const P = HIDDevice.prototype;

    const sendReport = P.sendReport;
    P.sendReport = function (reportId, data) {
        anota('ENVIA output', this, reportId, data);
        return sendReport.call(this, reportId, data);
    };

    const sendFeatureReport = P.sendFeatureReport;
    P.sendFeatureReport = function (reportId, data) {
        anota('ENVIA feature', this, reportId, data);
        return sendFeatureReport.call(this, reportId, data);
    };

    const receiveFeatureReport = P.receiveFeatureReport;
    P.receiveFeatureReport = async function (reportId) {
        const data = await receiveFeatureReport.call(this, reportId);
        anota('RECEBE feature', this, reportId, data);
        return data;
    };

    // Respostas que chegam sozinhas (input reports).
    const ouvidos = new WeakSet();
    const ouve = (device) => {
        if (ouvidos.has(device)) return;
        ouvidos.add(device);
        device.addEventListener('inputreport', e => anota('RECEBE input', device, e.reportId, e.data));
    };
    const open = P.open;
    P.open = async function () {
        await open.call(this);
        ouve(this);
    };
    navigator.hid.getDevices().then(list => list.filter(d => d.opened).forEach(ouve));

    const baixa = (nome, itens) => {
        const blob = new Blob([JSON.stringify(itens, null, 2)], { type: 'application/json' });
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = `${nome}.json`;
        a.click();
        setTimeout(() => URL.revokeObjectURL(a.href), 1000);
        console.log(`Salvo: ${nome}.json (${itens.length} pacotes)`);
    };

    window.marca = (nome) => { acao = nome; console.log(`Ação atual: ${nome}`); };
    window.salvar = () => baixa(acao, log.filter(i => i.acao === acao));
    window.salvarTudo = () => baixa('tudo', log);

    console.log('Espião HID ativo. Use marca("nome"), depois salvar().');
})();
