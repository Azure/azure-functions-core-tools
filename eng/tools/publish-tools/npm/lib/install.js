#! /usr/bin/env node

const fs = require('fs');
const https = require('https');
const { HttpsProxyAgent } = require('https-proxy-agent');
const os = require('os');
const path = require('path');
const ProgressBar = require('progress');
const yauzl = require('yauzl');
const { version } = require('../package.json');

function getRuntimeIdentifier() {
    const platforms = {
        darwin: 'osx',
        linux: 'linux',
        win32: 'win'
    };
    const architectures = {
        arm64: 'arm64',
        x64: 'x64'
    };
    const platform = platforms[os.platform()];
    const architecture = architectures[os.arch()];

    if (!platform || !architecture) {
        throw new Error(`Platform '${os.platform()}-${os.arch()}' is not supported.`);
    }

    return `${platform}-${architecture}`;
}

function getProxyAgent() {
    const proxy = process.env.npm_config_https_proxy
        || process.env.npm_config_proxy
        || process.env.HTTPS_PROXY
        || process.env.https_proxy
        || process.env.HTTP_PROXY
        || process.env.http_proxy;

    return proxy ? new HttpsProxyAgent(proxy) : undefined;
}

function download(url, destination, redirectCount = 0) {
    return new Promise((resolve, reject) => {
        const request = https.get(url, { agent: getProxyAgent() }, response => {
            if ([301, 302, 307, 308].includes(response.statusCode)) {
                response.resume();
                if (!response.headers.location || redirectCount >= 5) {
                    reject(new Error(`Unable to follow redirect while downloading '${url}'.`));
                    return;
                }

                resolve(download(new URL(response.headers.location, url), destination, redirectCount + 1));
                return;
            }

            if (response.statusCode !== 200) {
                response.resume();
                reject(new Error(`Failed to download '${url}'. Expected HTTP 200, received ${response.statusCode}.`));
                return;
            }

            const total = Number(response.headers['content-length']) || 0;
            const progress = total > 0
                ? new ProgressBar('[:bar] Downloading Azure Functions CLI', { total, width: 18 })
                : undefined;
            const file = fs.createWriteStream(destination);

            response.on('data', data => progress?.tick(data.length));
            response.on('error', reject);
            file.on('error', reject);
            file.on('finish', () => file.close(resolve));
            response.pipe(file);
        });

        request.on('error', reject);
        request.setTimeout(300000, () => {
            request.destroy(new Error(`Timed out downloading '${url}'.`));
        });
    });
}

function extractExecutable(archivePath, destination) {
    const expectedEntry = os.platform() === 'win32' ? 'func.exe' : 'func';

    return new Promise((resolve, reject) => {
        yauzl.open(archivePath, { lazyEntries: true }, (openError, archive) => {
            if (openError) {
                reject(openError);
                return;
            }

            archive.on('error', reject);
            archive.on('end', () => {
                reject(new Error(`Archive does not contain '${expectedEntry}'.`));
            });
            archive.on('entry', entry => {
                if (entry.fileName !== expectedEntry) {
                    archive.readEntry();
                    return;
                }

                archive.openReadStream(entry, (streamError, input) => {
                    if (streamError) {
                        archive.close();
                        reject(streamError);
                        return;
                    }

                    const output = fs.createWriteStream(destination, { mode: 0o755 });
                    input.on('error', reject);
                    output.on('error', reject);
                    output.on('close', () => {
                        archive.close();
                        resolve();
                    });
                    input.pipe(output);
                });
            });
            archive.readEntry();
        });
    });
}

async function install() {
    const runtimeIdentifier = getRuntimeIdentifier();
    const packageRoot = path.resolve(__dirname, '..');
    const binDirectory = path.join(packageRoot, 'bin');
    const archiveName = `Azure.Functions.Cli.${runtimeIdentifier}.${version}.zip`;
    const archivePath = path.join(binDirectory, archiveName);
    const executablePath = path.join(binDirectory, os.platform() === 'win32' ? 'func.exe' : 'func');
    const endpoint = `https://cdn.functions.azure.com/public/cli/v5/${version}/${archiveName}`;

    fs.rmSync(binDirectory, { recursive: true, force: true });
    fs.mkdirSync(binDirectory, { recursive: true });

    try {
        console.log(`Downloading Azure Functions CLI from '${endpoint}'.`);
        await download(endpoint, archivePath);
        await extractExecutable(archivePath, executablePath);
        fs.rmSync(archivePath, { force: true });
    } catch (error) {
        fs.rmSync(binDirectory, { recursive: true, force: true });
        throw error;
    }
}

install().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
});
