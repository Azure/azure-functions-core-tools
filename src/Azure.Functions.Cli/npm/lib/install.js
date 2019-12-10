#! /usr/bin/env node

const unzipper = require('unzipper');
const url = require('url');
const HttpsProxyAgent = require('https-proxy-agent');
const https = require('https');
const version = require('../package.json').version;
const chalk = require('chalk');
const path = require('path');
const fs = require('fs');
const rimraf = require('rimraf');
const glob = require('glob');
const execSync = require('child_process').execSync;
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
    platform = 'win-x64';
} else if (os.platform() === 'darwin') {
    platform = 'osx-x64';
} else if (os.platform() === 'linux') {
    platform = 'linux-x64';
} else {
    throw Error('platform ' + os.platform() + ' isn\'t supported');
}

const endpoint = 'https://functionscdn.azureedge.net/public/' + version + '/Azure.Functions.Cli.' + platform + '.' + version + '.zip';
console.log('attempting to GET %j', endpoint);
const options = url.parse(endpoint);
// npm config preceed system environment
// https://github.com/npm/npm/blob/19397ad523434656af3d3765e80e22d7e6305f48/lib/config/reg-client.js#L7-L8
// https://github.com/request/request/blob/b12a6245d9acdb1e13c6486d427801e123fdafae/lib/getProxyFromURI.js#L66-L71
const proxy = process.env.npm_config_https_proxy ||
            process.env.npm_config_proxy ||
            process.env.HTTPS_PROXY ||
            process.env.https_proxy ||
            process.env.HTTP_PROXY ||
            process.env.http_proxy;

const telemetryInfo = os.EOL
    + 'Telemetry' + os.EOL
    + '---------' + os.EOL
    + 'The Azure Functions Core tools collect usage data in order to help us improve your experience. ' + os.EOL
    + 'The data is anonymous and doesn\'t include any user specific or personal information. The data is collected by Microsoft.' + os.EOL
    + os.EOL
    + 'You can opt-out of telemetry by setting the FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT environment variable to \'1\' or \'true\' using your favorite shell.' + os.EOL

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
            response.on('data', data => bar.tick(data.length));
            const unzipStream = unzipper.Extract({ path: installPath })
                .on('close', () => {
                    try {
                        fs.closeSync(fs.openSync(`${installPath}/telemetryDefaultOn.sentinel`, 'w'))
                        console.log(telemetryInfo)
                    }
                    catch (err) {
                        // That's alright.
                    }
                    if (os.platform() === 'linux' || os.platform() === 'darwin') {
                        fs.chmodSync(`${installPath}/func`, 0o755);
                        fs.chmodSync(`${installPath}/gozip`, 0o755);
                    }
                });
            response.pipe(unzipStream);
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
