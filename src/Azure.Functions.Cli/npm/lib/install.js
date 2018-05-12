#! /usr/bin/env node

var unzipper = require('unzipper');
var url = require('url');
var HttpsProxyAgent = require('https-proxy-agent');
var https = require('https');
var version = require('../package.json').version;
var chalk = require('chalk');
var path = require('path');
var fs = require('fs');

var endpoint = 'https://functionscdn.azureedge.net/public/' + version + '/Azure.Functions.Cli.zip';
console.log('attempting to GET %j', endpoint);
var options = url.parse(endpoint);
// npm config preceed system environment
// https://github.com/npm/npm/blob/19397ad523434656af3d3765e80e22d7e6305f48/lib/config/reg-client.js#L7-L8
// https://github.com/request/request/blob/b12a6245d9acdb1e13c6486d427801e123fdafae/lib/getProxyFromURI.js#L66-L71
var proxy = process.env.npm_config_https_proxy || 
            process.env.npm_config_proxy ||
            process.env.HTTPS_PROXY ||
            process.env.https_proxy ||
            process.env.HTTP_PROXY ||
            process.env.http_proxy;

if (proxy) {
    console.log('using proxy server %j', proxy);
    options.agent = new HttpsProxyAgent(proxy);
}

https.get(options, function (response) {
        if (response.statusCode === 200) {
            var bin = path.join(path.dirname(fs.realpathSync(__filename)), '../bin');
            response
                .pipe(unzipper.Extract({
                    path: bin
                }));
        } else {
            console.error(chalk.red('Error downloading zip file from ' + endpoint));
            console.error(chalk.red('Expected: 200, Actual: ' + response.statusCode));
            process.exit(1);
        }
    })
    .on('error', function (err) {
        console.error(chalk.red(err));
        process.exit(1);
    });