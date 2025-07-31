const fs = require('fs');
const path = require('path');

const readMeSrc = path.resolve(__dirname, '..', '..', '..', '..', '..', 'README.md');
const readMeDest = path.resolve(__dirname, '..', 'README.md');

fs.copyFile(readMeSrc, readMeDest, (err) => {
    if (err) throw err;
    console.log('Copied README.md from the project root.');
});