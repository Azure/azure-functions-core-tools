const fs = require('fs');
const path = require('path');

const repositoryRoot = path.resolve(__dirname, '..', '..', '..', '..', '..');
const packageRoot = path.resolve(__dirname, '..');

for (const fileName of ['LICENSE', 'README.md']) {
    fs.copyFileSync(path.join(repositoryRoot, fileName), path.join(packageRoot, fileName));
}
