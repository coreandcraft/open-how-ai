const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const requireSource = createRequire(require('node:path').resolve('src/shared/i18n/i18n.ts'));
function load(stored, blocked = false) {
  const memory = new Map(stored ? [['journey.locale', stored]] : []);
  const document = { documentElement: {} };
  const exports = {};
  const code = ts.transpileModule(fs.readFileSync('src/shared/i18n/i18n.ts','utf8'), { compilerOptions: { module: ts.ModuleKind.CommonJS, esModuleInterop: true } }).outputText;
  vm.runInNewContext(code, { exports, require: requireSource, Intl, document,
    localStorage: { getItem: k => { if (blocked) throw Error(); return memory.get(k); }, setItem: (k,v) => { if(blocked) throw Error(); memory.set(k,v); } },
    window: { addEventListener() {} } });
  return { ...exports, document };
}
test('catalog keys and interpolation placeholders match English', () => {
  const en = require('./src/shared/i18n/locales/en.json');
  const placeholders = text => [...text.matchAll(/\{(\w+)\}/g)].map(m=>m[1]).sort();
  for (const locale of ['es','ja','zh-CN','ru']) {
    const translated = require(`./src/shared/i18n/locales/${locale}.json`);
    assert.deepEqual(Object.keys(translated).sort(),Object.keys(en).sort());
    for(const key of Object.keys(en)) { assert.ok(translated[key].trim(), `${locale}: ${key}`); assert.deepEqual(placeholders(translated[key]), placeholders(en[key]), `${locale}: ${key}`); }
  }
});
test('default, fallback, persistence and blocked browser storage', () => {
  const i = load('unsupported'); assert.equal(i.getLocale(),'en');
  assert.equal(i.t('Unknown diagnostic'),'Unknown diagnostic');
  i.setLocale('ja'); assert.equal(i.document.documentElement.lang,'ja'); assert.equal(i.t('Language'),'言語');
  assert.equal(load('ru').getLocale(),'ru');
  const blocked = load(null,true); blocked.setLocale('es'); assert.equal(blocked.t('Language'),'Idioma');
});
test('plural forms, interpolation and locale-aware date/number formatting', () => {
  const i=load('ru');
  for(const [n,expected] of [[1,'1 сообщение'],[2,'2 сообщения'],[5,'5 сообщений'],[21,'21 сообщение']])assert.equal(i.count('messages',n),expected);
  i.setLocale('en'); assert.equal(i.count('messages',1),'1 message'); assert.equal(i.count('messages',2),'2 messages');
  for (const locale of ['en','es','ja','zh-CN','ru']) {
    i.setLocale(locale);
    assert.equal(i.formatNumber(12345.6),new Intl.NumberFormat(locale).format(12345.6));
    assert.equal(i.formatDate('2026-10-08T12:00:00Z', { timeZone: 'UTC', dateStyle: 'long' }),new Intl.DateTimeFormat(locale,{ timeZone: 'UTC', dateStyle: 'long' }).format(new Date('2026-10-08T12:00:00Z')));
    assert.ok(i.t('Good to see you, {name}.',{name:'太郎'}).includes('太郎'));
  }
  assert.equal(i.formatDate('invalid'),'—');
});
