#! /usr/bin/env node

var unzipper = require('unzipper');
var https = require('https');
var version = require('../package.json').version;
var chalk = require('chalk');
var path = require('path');
var fs = require('fs');
var rimraf = require('rimraf');
var glob = require('glob');
var execSync = require('child_process').execSync;
var ProgressBar = require('progress');
var os = require('os');

function getPath() {
    var bin = path.resolve(path.join(path.dirname(__filename), '..', 'bin'));
    if (fs.existsSync(bin)) {
        rimraf.sync(bin);
    }
    return bin;
}

var platform = 'no-runtime';

if (os.platform() === 'win32') {
    platform = 'win-x64';
} else if (os.platform() === 'darwin') {
    platform = 'osx-x64';
} else if (os.platform() === 'linux') {
    platform = 'linux-x64';
} else {
    throw Error('platform ' + os.platform() + ' isn\'t supported');
}

var url = 'https://functionscdn.azureedge.net/public/' + version + '/Azure.Functions.Cli.' + platform + '.' + version + '.zip';
https.get(url, function (response) {

        var bar = new ProgressBar('[:bar] Downloading Azure Functions Cli', {
            total: Number(response.headers['content-length']),
            width: 18
        });

        if (response.statusCode === 200) {
            var installPath = getPath();
            response.on('data', function (data) {
                bar.tick(data.length);
            })
            var unzipStream = unzipper.Extract({
                    path: installPath
                })
                .on('close', () => {
                    installWorkers(installPath)
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
    .on('error', function (err) {
        console.error(chalk.red(err));
        process.exit(1);
    });

function installWorkers(installPath) {
    glob(`${installPath}/workers/*/*.targets`, (err, files) => {
        if (err)
            return console.error(chalk.red(err));
        files.forEach(runTarget);
    });
}

function runTarget(targetsFile) {
    var workingDirectory = path.dirname(targetsFile);
    console.log(`Language worker install targets found in '${workingDirectory}'.`);
    console.log(`Executing 'dotnet msbuild ${targetsFile}'.`);
    execSync(`dotnet msbuild ${targetsFile}`, {
        cwd: workingDirectory,
        stdio: [0, 1, 2]
    });
}