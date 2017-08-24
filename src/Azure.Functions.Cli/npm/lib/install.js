#! /usr/bin/env node

var unzipper = require('unzipper');
var https = require('https');
var version = require('../package.json').version;
var chalk = require('chalk');
var path = require('path');
var fs = require('fs');
var os = require('os');
var rimraf = require('rimraf');

function getPath() {
    var bin = path.join(os.homedir(), '.azurefunctions', 'bin');
    if (fs.existsSync(bin)) {
        rimraf.sync(bin);
    }
    return bin;
}


var url = 'https://functionscdn.azureedge.net/public/' + version + '/Azure.Functions.Cli.zip';
https.get(url, function (response) {
        if (response.statusCode === 200) {
            response
                .pipe(unzipper.Extract({
                    path: getPath()
                }));
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