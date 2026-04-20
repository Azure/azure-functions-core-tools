#! /usr/bin/env node

const extract = require('extract-zip');
const url = require('url');
const HttpsProxyAgent = require('https-proxy-agent');
const https = require('https');
const version = require('../package.json').version;
const chalk = require('chalk');
const path = require('path');
const fs = require('fs');
const rimraf = require('rimraf');
const ProgressBar = require('progress');
const os = require('os');

function getPath() {
    const bin = path.resolve(path.join(path.dirname(__filename), '..', 'bin'));
    if (fs.existsSync(bin)) {
        rimraf.sync(bin);
    }
    return bin
}

let platform = '';

if (os.platform() === 'win32') {
    if (os.arch() === 'arm64') {
        platform = 'win-arm64';
    } else {
        platform = 'win-x64';
    }
} else if (os.platform() === 'darwin') {
    if (os.arch() === 'arm64') {
        platform = 'osx-arm64';
    } else {
        platform = 'osx-x64';
    }
} else if (os.platform() === 'linux') {
    if (os.arch() === 'arm64') {
        platform = 'linux-arm64';
    } else {
        platform = 'linux-x64';
    }
} else {
    throw Error('platform ' + os.platform() + ' isn\'t supported');
}

const fileName = 'Azure.Functions.Cli.' + platform + '.' + version + '.zip';
const endpoint = 'https://cdn.functions.azure.com/public/' + version + '/' + fileName;

console.log('attempting to GET %j', endpoint);
const options = url.parse(endpoint);

const proxy = process.env.npm_config_https_proxy ||
            process.env.npm_config_proxy ||
            process.env.HTTPS_PROXY ||
            process.env.https_proxy ||
            process.env.HTTP_PROXY ||
            process.env.http_proxy;

if (proxy) {
    console.log('using proxy server %j', proxy);
    options.agent = new HttpsProxyAgent(proxy);
}

https.get(options, response => {
    const bar = new ProgressBar('[:bar] Downloading Azure Functions Core Tools', {
        total: Number(response.headers['content-length']),
        width: 18
    });

    if (response.statusCode === 200) {
        const installPath = getPath();
        const downloadPath = installPath + '/' + fileName;
        response.on('data', data => bar.tick(data.length));
        if (!fs.existsSync(installPath)) {
            fs.mkdirSync(installPath);
        }
        const file = fs.createWriteStream(downloadPath);
        response.pipe(file);
        file.on('finish', function() {
            file.close(() => {
                extract(file.path, {
                    dir: installPath
                }).then(() => {
                    try {
                        fs.unlinkSync(downloadPath);
                    }
                    catch (err) {
                        // That's alright.
                    }

                    const platform = os.platform();

                    if (platform === 'linux' || platform === 'darwin') {
                        fs.chmodSync(`${installPath}/func`, 0o755);
                    }
                });
            });
        });
    } else {
        console.error(chalk.red('Error downloading zip file from ' + endpoint));
        console.error(chalk.red('Expected: 200, Actual: ' + response.statusCode));
        process.exit(1);
    }
})
.on('error', err => {
    console.error(chalk.red(err));
    process.exit(1);
});
