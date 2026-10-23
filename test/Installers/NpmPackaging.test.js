const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');
const { test } = require('node:test');

const repositoryRoot = path.resolve(__dirname, '..', '..');
const script = path.join(repositoryRoot, 'eng', 'scripts', 'pack-npm.ps1');
const packageSource = path.join(repositoryRoot, 'eng', 'tools', 'publish-tools', 'npm');

function run(command, args, options = {}) {
    return new Promise((resolve, reject) => {
        const child = spawn(command, args, { ...options, windowsHide: true });
        let output = '';
        child.stdout.on('data', data => { output += data; });
        child.stderr.on('data', data => { output += data; });
        child.on('error', reject);
        child.on('close', code => resolve({ code, output }));
    });
}

async function createFixture(t) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'func-npm-pack-'));
    t.after(() => fs.rmSync(root, { recursive: true, force: true }));
    const packageDirectory = path.join(root, 'repo', 'eng', 'tools', 'publish-tools', 'npm');
    fs.mkdirSync(packageDirectory, { recursive: true });
    for (const name of ['package.json', 'npm-shrinkwrap.json', 'lib']) {
        fs.cpSync(path.join(packageSource, name), path.join(packageDirectory, name), { recursive: true });
    }
    for (const name of ['LICENSE', 'README.md']) {
        fs.copyFileSync(path.join(repositoryRoot, name), path.join(root, 'repo', name));
    }

    const requests = [];
    const registry = http.createServer((request, response) => {
        requests.push(`${request.method} ${request.url}`);
        response.writeHead(500);
        response.end('Registry access is forbidden during packaging.');
    });
    await new Promise(resolve => registry.listen(0, '127.0.0.1', resolve));
    t.after(() => new Promise(resolve => registry.close(resolve)));
    const cache = path.join(root, 'empty-cache');
    const env = {
        ...process.env,
        npm_config_cache: cache,
        npm_config_registry: `http://127.0.0.1:${registry.address().port}`,
        npm_config_fetch_retries: '0',
        npm_config_fetch_timeout: '2000',
        npm_config_ignore_scripts: 'false'
    };
    const outputDirectory = path.join(root, 'output');
    return { packageDirectory, outputDirectory, env, requests };
}

for (const version of ['  v5.0.0-preview.4  ', '5.0.0']) {
    test(`packs both npm identities offline for ${version.trim()}`, { timeout: 60000 }, async t => {
        const fixture = await createFixture(t);
        const expectedVersion = version.trim().replace(/^v/i, '');
        const originalManifest = JSON.parse(fs.readFileSync(path.join(packageSource, 'package.json')));
        const originalLock = JSON.parse(fs.readFileSync(path.join(packageSource, 'npm-shrinkwrap.json')));
        const result = await run('pwsh', [
            '-NoLogo', '-NoProfile', '-NonInteractive', '-Command',
            '& $env:PACK_SCRIPT -PackageDirectory $env:PACK_DIRECTORY -Version $env:PACK_VERSION ' +
            '-PackageNames azure-functions-core-tools,azure-functions-cli -OutputDirectory $env:PACK_OUTPUT'
        ], {
            env: {
                ...fixture.env,
                PACK_SCRIPT: script,
                PACK_DIRECTORY: fixture.packageDirectory,
                PACK_VERSION: version,
                PACK_OUTPUT: fixture.outputDirectory
            }
        });
        assert.equal(result.code, 0, result.output);
        assert.deepEqual(fixture.requests, [], 'Packaging must not contact the registry.');
        assert.equal(fs.existsSync(path.join(fixture.packageDirectory, 'node_modules')), false);
        assert.equal(fs.existsSync(path.join(fixture.packageDirectory, 'package-lock.json')), false);

        for (const name of ['azure-functions-core-tools', 'azure-functions-cli']) {
            const archiveDirectory = path.join(fixture.outputDirectory, name);
            const archiveName = `${name}-${expectedVersion}.tgz`;
            assert.deepEqual(fs.readdirSync(archiveDirectory), [archiveName]);
            const archive = path.join(archiveDirectory, archiveName);
            const manifestResult = await run('tar', ['-xOf', archive, 'package/package.json']);
            assert.equal(manifestResult.code, 0, manifestResult.output);
            assert.deepEqual(JSON.parse(manifestResult.output), {
                ...originalManifest, name, version: expectedVersion
            });

            const lockResult = await run('tar', ['-xOf', archive, 'package/npm-shrinkwrap.json']);
            assert.equal(lockResult.code, 0, lockResult.output);
            assert.deepEqual(JSON.parse(lockResult.output), {
                ...originalLock,
                name,
                version: expectedVersion,
                packages: {
                    ...originalLock.packages,
                    '': { ...originalLock.packages[''], name, version: expectedVersion }
                }
            });

            const listing = await run('tar', ['-tzf', archive]);
            assert.equal(listing.code, 0, listing.output);
            assert.doesNotMatch(listing.output, /node_modules|package-lock\.json/);
            for (const file of ['lib/main.js', 'lib/install.js', 'lib/copy-metadata.js', 'LICENSE', 'README.md']) {
                assert.ok(listing.output.split(/\r?\n/).includes(`package/${file}`), `Missing ${file}`);
            }
        }
    });
}

test('rejects an invalid version without registry access or artifacts', { timeout: 30000 }, async t => {
    const fixture = await createFixture(t);
    const result = await run('pwsh', [
        '-NoLogo', '-NoProfile', '-NonInteractive', '-File', script,
        '-PackageDirectory', fixture.packageDirectory,
        '-Version', 'not-a-version',
        '-PackageNames', 'azure-functions-core-tools',
        '-OutputDirectory', fixture.outputDirectory
    ], { env: fixture.env });
    assert.notEqual(result.code, 0, result.output);
    assert.match(result.output, /Failed to set npm package version/);
    assert.deepEqual(fixture.requests, []);
    assert.equal(fs.existsSync(fixture.outputDirectory), false);
});

test('surfaces prepack failures without registry access', { timeout: 30000 }, async t => {
    const fixture = await createFixture(t);
    fs.unlinkSync(path.resolve(fixture.packageDirectory, '..', '..', '..', '..', 'README.md'));
    const result = await run('pwsh', [
        '-NoLogo', '-NoProfile', '-NonInteractive', '-File', script,
        '-PackageDirectory', fixture.packageDirectory,
        '-Version', '5.0.0-preview.4',
        '-PackageNames', 'azure-functions-core-tools',
        '-OutputDirectory', fixture.outputDirectory
    ], { env: fixture.env });
    assert.notEqual(result.code, 0, result.output);
    assert.match(result.output, /Failed to create the 'azure-functions-core-tools' npm package/);
    assert.deepEqual(fixture.requests, []);
    assert.deepEqual(fs.readdirSync(path.join(fixture.outputDirectory, 'azure-functions-core-tools')), []);
});
