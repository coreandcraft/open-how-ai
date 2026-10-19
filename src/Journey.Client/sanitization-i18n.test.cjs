const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const keys = ["Automatic replacements","Exact, case-sensitive text. Longer phrases match first; replacements are applied once.","Find text","Replace with","Remove replacement","Add replacement","[REDACTED]","Sanitize sensitive information","Your account's saved rules are loaded for publishing and optional import sanitization.","Replacement rules saved","Save replacement rules","Sanitize sensitive information before import","Sanitizes text source files before storage, including filenames and metadata. Archives retain extracted files. Files that cannot be sanitized require your permission to retain originals. Existing imports are not changed.","Also redact common credential, email and local path patterns","Changes here apply only to this import. Review the sanitized preview before importing. Pattern scanning is not exhaustive.","SANITIZED PREVIEW","Some files need your permission","Some files cannot be sanitized. Keeping them will store their original contents, which may include sensitive information. This permission applies only to the selected upload.","Keep original files and continue","Original files retained with your permission","These files were not sanitized and may contain sensitive information.","Review sanitized copies and any originals you explicitly allowed. Pattern scanning cannot identify every sensitive value.","Some files cannot be sanitized. Review the warning before keeping originals.","Provide valid sanitization options.","Use at most 100 replacement rules with nonblank search text and values of at most 1000 characters.","Each search phrase must be unique.","Sanitization would create duplicate JSON field names. Adjust the replacement rules."];

test('sanitization controls, consent and validation messages have real translations', () => {
  for (const locale of ['en', 'es', 'ja', 'zh-CN', 'ru']) {
    const catalog = require(`./src/shared/i18n/locales/${locale}.json`);
    for (const key of keys) {
      assert.ok(catalog[key]?.trim(), `${locale} missing: ${key}`);
      if (locale !== 'en') assert.notEqual(catalog[key], key, `${locale} falls back to English: ${key}`);
    }
  }
});

test('every replacement settings translation key is present in every catalog', () => {
  const path = 'src/features/imports/SanitizationSettings.tsx';
  const source = ts.createSourceFile(path, fs.readFileSync(path, 'utf8'), ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const found = [];
  function visit(node) {
    if (ts.isCallExpression(node) && node.expression.getText(source) === 't' && ts.isStringLiteral(node.arguments[0])) found.push(node.arguments[0].text);
    ts.forEachChild(node, visit);
  }
  visit(source);
  for (const locale of ['en', 'es', 'ja', 'zh-CN', 'ru']) {
    const catalog = require(`./src/shared/i18n/locales/${locale}.json`);
    for (const key of found) assert.ok(catalog[key], `${locale} missing: ${key}`);
  }
});
