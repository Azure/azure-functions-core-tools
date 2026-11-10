import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, readFile, rm, symlink, writeFile } from 'node:fs/promises';
import { createRequire, stripTypeScriptTypes } from 'node:module';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = fileURLToPath(new URL('../../../', import.meta.url));
const connectors = [
    { name: 'Outlook', label: 'Office 365 trigger', setting: 'OnNewEmailTriggerConfigName', sdk: 'Office365', key: 'OnNewEmail', operation: 'OnNewEmailV3' },
    { name: 'Sharepoint', label: 'SharePoint trigger', setting: 'OnSharepointNewFileTriggerConfigName', sdk: 'Sharepointonline', key: 'OnNewFileItems', operation: 'GetOnNewFileItems' },
    { name: 'OneDrive', label: 'OneDrive trigger', setting: 'OnOneDriveNewFileTriggerConfigName', sdk: 'Onedriveforbusiness', key: 'OnNewFiles', operation: 'OnNewFilesV2' },
    { name: 'Dataverse', label: 'Dataverse trigger', setting: 'OnDataverseRowChangedTriggerConfigName', sdk: 'Commondataservice', key: 'OnNewItems', operation: 'GetOnNewItems_V2' },
    { name: 'Teams', label: 'Teams trigger', setting: 'OnNewChannelMessageTriggerConfigName', sdk: 'Teams', key: 'OnNewChannelMessage', operation: 'OnNewChannelMessage' },
];
const languages = { JavaScript: 'js', TypeScript: 'ts' };
const catalog = JSON.parse((await readFile(path.join(root, 'src/Cli/func/StaticResources/node-v4-templates.json'), 'utf8')).replace(/^\uFEFF/, ''));
const temporary = await mkdtemp(path.join(os.tmpdir(), 'node-connector-templates-'));
const dependencyApp = process.env.NODE_CONNECTOR_TEST_DEPENDENCIES;
const generatedTypeScript = [];
const javascriptDirectory = path.join(temporary, 'javascript');

try {
    await mkdir(javascriptDirectory);
    await writeFile(path.join(javascriptDirectory, 'package.json'), JSON.stringify({ type: 'commonjs' }));
    if (dependencyApp) {
        await symlink(path.resolve(dependencyApp, 'node_modules'), path.join(temporary, 'node_modules'), process.platform === 'win32' ? 'junction' : 'dir');
    }

    await test('runtime dependencies provide Functions 4.17.1 enums and binding defaults', { skip: !dependencyApp }, () => {
        const require = createRequire(path.join(temporary, 'package.json'));
        const version = require('@azure/functions/package.json').version;
        const match = version.match(/^4\.(\d+)\.(\d+)(?:\+[\w.-]+)?$/);
        assert.ok(match, 'The template requires a stable Functions v4 release.');
        assert.ok(Number(match[1]) > 17 || (Number(match[1]) === 17 && Number(match[2]) >= 1), 'The template requires Functions 4.17.1 or later.');
        const { DeliveryMode, Cardinality, trigger } = require('@azure/functions');
        assert.deepEqual(DeliveryMode, { Webhook: 'Webhook', Poll: 'Poll' });
        assert.deepEqual(Cardinality, { One: 'one', Many: 'many' });
        const binding = trigger.connectorTrigger({});
        assert.equal(binding.deliveryMode, DeliveryMode.Webhook);
        assert.equal(binding.cardinality, Cardinality.One);
    });

    await test('the built-in catalog contains exactly ten unique connector templates', () => {
        const templates = catalog.filter((template) => template.metadata.triggerType === 'connectorTrigger');
        assert.equal(templates.length, 10);
        assert.equal(new Set(templates.map((template) => template.id)).size, 10);
    });

    await test('Windows and Linux PR unit jobs invoke connector checks with Node 24', async () => {
        const steps = await readFile(path.join(root, 'eng/ci/templates/public/steps/run-unit-tests.yml'), 'utf8');
        assert.match(steps, /task: NodeTool@0[\s\S]*?versionSpec: '24\.x'/);
        assert.ok(steps.includes("Join-Path $PWD 'test' 'Cli' 'Func.UnitTests' 'node-connector-templates.test.mjs'"));
        assert.ok(steps.includes('node --test $testFile'));
        for (const platform of ['windows', 'linux']) {
            const job = await readFile(path.join(root, 'eng/ci/templates/public/jobs', `test-unit-${platform}.yml`), 'utf8');
            assert.ok(job.includes('template: /eng/ci/templates/public/steps/run-unit-tests.yml@self'));
        }
    });

    await test('the guide explicitly limits JavaScript templates to CommonJS projects', async () => {
        const guide = await readFile(path.join(root, 'docs/connector-templates.md'), 'utf8');
        assert.match(guide, /CommonJS-only/);
        assert.ok(guide.includes('"type": "module"'));
        assert.match(guide, /not filtered by package module type/);
    });

    for (const connector of connectors) {
        let expectedPayloadDocumentation;

        for (const [language, extension] of Object.entries(languages)) {
            const id = `${connector.name}Trigger-${language}-4.x`;
            const templates = catalog.filter((template) => template.id === id);
            assert.equal(templates.length, 1, `Missing or duplicated built-in template ${id}.`);
            const template = templates[0];
            const metadata = template.metadata;
            const fileName = `%functionName%.${extension}`;
            const source = template.files[fileName];
            const payloadDocumentation = [...source.matchAll(/^\/\/ (\w+) - (\w+)(?:\[\])?$/gm)]
                .map((match) => ({ operation: match[1], model: match[2] }));

            await test(`${id}: built-in v4 metadata and file shape are complete`, () => {
                assert.equal(template.runtime, '2');
                assert.equal(template.function, undefined);
                assert.equal(metadata.language, language);
                assert.equal(metadata.name, connector.label);
                assert.equal(metadata.triggerType, 'connectorTrigger');
                assert.equal(metadata.enabledInTryMode, false);
                assert.deepEqual(metadata.userPrompt, []);
                assert.equal(typeof metadata.description, 'string');
                assert.ok(metadata.description.length > 0 && !metadata.description.startsWith('$'), 'Descriptions must not require resources added to an extension bundle.');
                assert.match(metadata.defaultFunctionName, /^[a-zA-Z][a-zA-Z0-9_]{0,127}$/);
                assert.deepEqual(Object.keys(template.files), [fileName]);
                assert.match(source, /deliveryMode: DeliveryMode\.Poll/);
                assert.doesNotMatch(source, /\bcardinality\s*:/, 'Single-event templates must rely on the library default.');
                assert.ok(source.includes(`triggerConfigName: '%${connector.setting}%'`));
                assert.doesNotMatch(source, /\bany\b|console\.log|function\.json|deliveryMode: 'Webhook'/);
                assert.ok(payloadDocumentation.length >= 2, 'List the selected and alternative operation payloads.');
                assert.ok(payloadDocumentation.some(({ operation }) => operation === connector.operation));
                if (expectedPayloadDocumentation) {
                    assert.deepEqual(payloadDocumentation, expectedPayloadDocumentation, 'Both languages must document the same operation/model mappings.');
                } else {
                    expectedPayloadDocumentation = payloadDocumentation;
                }
            });

            for (const name of [metadata.defaultFunctionName, `custom${connector.name}Poll`]) {
                const generated = source.replaceAll('%functionName%', name);
                const runtimeSource = language === 'TypeScript' ? stripTypeScriptTypes(generated) : generated;
                const generatedPath = language === 'TypeScript'
                    ? path.join(temporary, `${name}-${language}.mjs`)
                    : path.join(javascriptDirectory, `${name}.js`);
                await writeFile(generatedPath, runtimeSource);
                if (language === 'TypeScript') {
                    const typeScriptPath = path.join(temporary, `${name}.ts`);
                    await writeFile(typeScriptPath, generated);
                    generatedTypeScript.push(typeScriptPath);
                }

                await test(`${id}: generates valid code with function name ${name}`, () => {
                    assert.ok(!generated.includes('%functionName%'));
                    assert.ok(generated.includes(`'${name}'`));
                    const result = spawnSync(process.execPath, ['--check', generatedPath], { encoding: 'utf8' });
                    assert.ifError(result.error);
                    assert.equal(result.status, 0, result.stderr);
                });

                await test(`${id}: ${name} registers and normalizes real SDK payloads`, { skip: !dependencyApp }, async () => {
                    const require = createRequire(generatedPath);
                    const { app, trigger, DeliveryMode, Cardinality } = require('@azure/functions');
                    const sdk = require(`@azure/connectors/generated/${connector.sdk}Extensions`);
                    assert.equal(sdk[`${connector.sdk}TriggerOperations`][connector.key], connector.operation);
                    const operations = new Set(Object.values(sdk[`${connector.sdk}TriggerOperations`]));
                    const excludedOperations = new Set(['OnNewFile', 'OnUpdatedFile', 'OnNewFileV2', 'OnUpdatedFileV2']);
                    const expectedOperations = new Set([...operations].filter((operation) => !excludedOperations.has(operation)));
                    assert.deepEqual(new Set(payloadDocumentation.map(({ operation }) => operation)), expectedOperations, 'Document all exported metadata operations, excluding deprecated SharePoint and raw OneDrive content operations.');
                    const declarations = await readFile(require.resolve(`@azure/connectors/generated/${connector.sdk}Extensions`).replace(/\.js$/, '.d.ts'), 'utf8');
                    for (const { operation, model } of payloadDocumentation) {
                        assert.ok(operations.has(operation), `Missing SDK operation ${operation}.`);
                        assert.match(declarations, new RegExp(`export (?:interface|type) ${model}\\b`), `Missing SDK payload model ${model}.`);
                    }
                    const original = app.connectorTrigger;
                    const registrations = [];
                    app.connectorTrigger = (registeredName, options) => registrations.push({ registeredName, options });
                    try {
                        await import(pathToFileURL(generatedPath).href);
                    } finally {
                        app.connectorTrigger = original;
                    }
                    assert.equal(registrations.length, 1);
                    const { registeredName, options: { handler, ...options } } = registrations[0];
                    assert.equal(registeredName, name);
                    assert.deepEqual(options, {
                        deliveryMode: DeliveryMode.Poll,
                        connection: 'ConnectorNamespace',
                        triggerConfigName: `%${connector.setting}%`,
                    });
                    const binding = trigger.connectorTrigger(options);
                    assert.equal(binding.deliveryMode, DeliveryMode.Poll);
                    assert.equal(binding.cardinality, Cardinality.One);
                    const item = { secretField: 'do-not-log-this-payload' };
                    const payloads = [
                        [{ body: item }, 1],
                        [{ body: { value: [item, item] } }, 2],
                        [[{ body: item }, { body: { value: [item] } }], 2],
                        [JSON.stringify([{ body: item }, { body: item }]), 2],
                        [[], 0],
                        [null, 0],
                    ];
                    for (const [payload, count] of payloads) {
                        const logs = [];
                        await handler(payload, {
                            log: (...args) => logs.push(args.join(' ')),
                            warn: (...args) => assert.fail(args.join(' ')),
                        });
                        assert.equal(logs.length, 1);
                        assert.ok(logs[0].includes(`Received ${count} `));
                        assert.ok(!logs[0].includes(item.secretField));
                    }
                });
            }
        }
    }

    await test('all generated TypeScript modules pass strict SDK type checking', { skip: !dependencyApp }, () => {
        const require = createRequire(path.join(temporary, 'package.json'));
        const compiler = require.resolve('typescript/bin/tsc');
        const result = spawnSync(process.execPath, [
            compiler, '--noEmit', '--strict', '--module', 'Node16',
            '--moduleResolution', 'Node16', '--target', 'ES2022',
            ...generatedTypeScript,
        ], { encoding: 'utf8', cwd: temporary });
        assert.ifError(result.error);
        assert.equal(result.status, 0, result.stdout + result.stderr);
    });
} finally {
    await rm(temporary, { recursive: true, force: true });
}
