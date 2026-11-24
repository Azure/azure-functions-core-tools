import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { cpSync, existsSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const javaScriptTemplate = fileURLToPath(new URL('../../../../src/Workloads/Templates/Node/javascript/project', import.meta.url));
const typeScriptTemplate = fileURLToPath(new URL('../../../../src/Workloads/Templates/Node/typescript/project', import.meta.url));

function createProject(template, root) {
    const hive = join(root, 'hive');
    const project = join(root, 'project');
    execFileSync('dotnet', ['new', 'install', template, '--debug:custom-hive', hive]);
    execFileSync('dotnet', ['new', 'func-node', '--name', 'app', '--output', project, '--debug:custom-hive', hive]);
    return project;
}

function runNpm(args, cwd) {
    if (process.platform === 'win32') {
        execFileSync(process.env.ComSpec || 'cmd.exe', ['/d', '/s', '/c', `npm.cmd ${args.join(' ')}`], { cwd });
    } else {
        execFileSync('npm', args, { cwd });
    }
}

test('dotnet new skips the Node restore action for JavaScript projects', () => {
    const root = mkdtempSync(join(tmpdir(), 'func-node-template-test-'));
    try {
        const project = createProject(javaScriptTemplate, root);
        assert.ok(existsSync(join(project, 'package.json')));
    } finally {
        rmSync(root, { recursive: true, force: true });
    }
});

test('a generated TypeScript project removes deleted functions on rebuild', () => {
    const root = mkdtempSync(join(tmpdir(), 'func-node-template-test-'));
    try {
        const project = createProject(typeScriptTemplate, root);

        if (process.env.NODE_TEMPLATE_TEST_NODE_MODULES) {
            cpSync(process.env.NODE_TEMPLATE_TEST_NODE_MODULES, join(project, 'node_modules'), { recursive: true });
        } else {
            runNpm(['install', '--no-audit', '--no-fund'], project);
        }

        const source = join(project, 'src', 'functions', 'HelloWorld.ts');
        const output = join(project, 'dist', 'src', 'functions', 'HelloWorld.js');
        runNpm(['run', 'build'], project);
        assert.ok(existsSync(output));

        rmSync(source);
        runNpm(['run', 'build'], project);
        assert.equal(existsSync(output), false);
    } finally {
        rmSync(root, { recursive: true, force: true });
    }
});
