#! /usr/bin/env node

const { spawn, spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const packageRoot = path.resolve(__dirname, '..');
const executable = path.join(packageRoot, 'bin', process.platform === 'win32' ? 'func.exe' : 'func');

if (!fs.existsSync(executable)) {
    console.log('Azure Functions CLI binary not found. Running first-time setup...');
    const result = spawnSync(process.execPath, [path.join(__dirname, 'install.js')], { stdio: 'inherit' });
    if (result.status !== 0) {
        process.exit(result.status || 1);
    }
}

const child = spawn(executable, process.argv.slice(2), {
    stdio: 'inherit'
});
const signalHandlers = new Map();

child.on('error', error => {
    console.error(`Failed to start Azure Functions CLI: ${error.message}`);
    process.exitCode = 1;
});

child.on('exit', (code, signal) => {
    if (signal) {
        const handler = signalHandlers.get(signal);
        if (handler) {
            process.off(signal, handler);
        }
        process.kill(process.pid, signal);
        return;
    }

    process.exitCode = code ?? 1;
});

for (const signal of ['SIGINT', 'SIGTERM']) {
    const handler = () => child.kill(signal);
    signalHandlers.set(signal, handler);
    process.on(signal, handler);
}
