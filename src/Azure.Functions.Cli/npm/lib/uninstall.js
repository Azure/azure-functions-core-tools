#! /usr/bin/env node

var path = require('path');
var fs = require('fs');
var os = require('os');
var rimraf = require('rimraf');


var bin = path.join(os.homedir(), '.azurefunctions', 'bin');
if (fs.existsSync(bin)) {
    console.log('deleting ' + bin);
    rimraf.sync(bin);
}