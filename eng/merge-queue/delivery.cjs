const { SHA, TICKET, invariant } = require('./policy.cjs');

const PROFILE = 'independent-v1';
const RELEASE_SCHEMA = 'completed-source-v1';

function manifestValid(value) {
  if (value.manifest === undefined) return value.admission === undefined;
  const admission = value.admission;
  return /^[a-f0-9]{64}$/.test(value.manifest) && admission?.schema === 'batch-manifest-v1'
    && admission.ticket === value.ticket && admission.base === value.base && admission.combined === value.combined
    && admission.inventory?.blob === value.validation.inventory
    && admission.inventory?.schema === value.validation.schema
    && Array.isArray(admission.prefixes) && admission.prefixes.length > 0
    && admission.prefixes.at(-1).sha === value.combined && admission.prefixes.at(-1).tree === value.tree
    && require('./batch-policy.cjs').digest(admission) === value.manifest;
}

function independent(value = '') {
  invariant(['', PROFILE].includes(value), 'Unknown merge queue delivery profile.');
  return value === PROFILE;
}

function releaseRecord(tip) {
  invariant(tip?.delivery === PROFILE && SHA.test(tip.sha) && SHA.test(tip.tree)
    && TICKET.test(tip.ticket) && SHA.test(tip.base) && SHA.test(tip.combined)
    && Number.isSafeInteger(tip.validation?.run) && tip.validation.run > 0
    && tip.validation.attempt === 1 && tip.validation.source === tip.combined
    && tip.validation.workflow === tip.base && SHA.test(tip.validation.inventory)
    && ['windows-12-delivery-v1', 'unit-pr-v1'].includes(tip.validation.schema)
    && manifestValid(tip),
  'Invalid independent completed-source proof.');
  return { schema: RELEASE_SCHEMA, source: tip.sha, tree: tip.tree, ticket: tip.ticket,
    base: tip.base, combined: tip.combined, validation: tip.validation,
    ...(tip.manifest ? { manifest: tip.manifest, admission: tip.admission } : {}) };
}

function validateRelease(record, source) {
  invariant(record?.schema === RELEASE_SCHEMA && record.source === source && SHA.test(source)
    && SHA.test(record.tree) && SHA.test(record.base) && SHA.test(record.combined)
    && TICKET.test(record.ticket) && Number.isSafeInteger(record.validation?.run)
    && record.validation.run > 0 && record.validation.attempt === 1
    && record.validation.source === record.combined && record.validation.workflow === record.base
    && SHA.test(record.validation.inventory) && ['windows-12-delivery-v1', 'unit-pr-v1'].includes(record.validation.schema)
    && manifestValid(record),
  'Invalid independent completed-source record.');
  return record;
}

function packagingSelection(files) {
  if (!Array.isArray(files) || files.length === 0 || files.length >= 300
    || files.some(file => !file || typeof file.filename !== 'string'
      || !['added', 'removed', 'modified', 'renamed', 'copied', 'changed', 'unchanged'].includes(file.status)
      || file.status === 'renamed' && typeof file.previous_filename !== 'string'))
    return { build: true, reason: 'unknown-impact' };
  const paths = files.flatMap(file => [file.filename, ...(file.previous_filename ? [file.previous_filename] : [])]);
  const packaging = /^(?:installer\/|src\/Kora\/Assets\/|\.github\/workflows\/|eng\/|Directory\.|global\.json$|GitVersion\.yml$|NuGet\.Config$|LICENSE$|THIRD-PARTY-NOTICES\.md$|DEPENDENCY-LICENSES\.md$)|(?:^|\/)(?:packages\.lock\.json|[^/]+\.(?:csproj|wixproj|props|targets))$/i;
  const ordinary = /^(?:src\/|tests\/|Design\/|docs\/|\.github\/instructions\/)|^[^/]+\.md$/;
  const build = paths.some(path => packaging.test(path) || !ordinary.test(path));
  return { build, reason: build ? 'packaging-or-unknown-impact' : 'ordinary-source-main-packaging' };
}

module.exports = { PROFILE, RELEASE_SCHEMA, independent, releaseRecord,
  validateRelease, packagingSelection };
