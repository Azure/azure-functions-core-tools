#! /usr/bin/env node

const unzipper = require('unzipper');
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

const url = 'https://functionscdn.azureedge.net/public/' + version + '/Azure.Functions.Cli.' + platform + '.' + version + '.zip';
https.get(url, response => {

        const bar = new ProgressBar('[:bar] Downloading Azure Functions Cli', { 
            total: Number(response.headers['content-length']),
            width: 18
        });

        if (response.statusCode === 200) {
            const installPath = getPath();
            response.on('data', data => bar.tick(data.length));
            const unzipStream = unzipper.Extract({ path: installPath })
                .on('close', () => {
                    if (os.platform() === 'linux' || os.platform() === 'darwin') {
                        fs.chmodSync(`${installPath}/func`, 0o755);
                    }
                });
            response.pipe(unzipStream);
        } else {
            console.error(chalk.red('Error downloading zip file from ' + url));
            console.error(chalk.red('Expected: 200, Actual: ' + response.statusCode));
            process.exit(1);
        }
    })
    .on('error', err => {
        console.error(chalk.red(err));
        process.exit(1);
    });
