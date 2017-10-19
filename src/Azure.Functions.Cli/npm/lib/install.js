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

function getPath() {
    var bin = path.resolve(path.join(path.dirname(__filename), '..', 'bin'));
    if (fs.existsSync(bin)) {
        rimraf.sync(bin);
    }
    return bin;
}

var url = 'https://functionscdn.azureedge.net/public/' + version + '/Azure.Functions.Cli.zip';
https.get(url, function (response) {
        if (response.statusCode === 200) {
            var installPath = getPath();
            var unzipStream = unzipper.Extract({ path: installPath })
                .on('close', () => installWorkers(installPath));
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
  execSync(`dotnet msbuild ${targetsFile}`, { cwd: workingDirectory, stdio: [0, 1, 2] });
}