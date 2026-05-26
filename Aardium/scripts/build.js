const { packager } = require('@electron/packager');
const { execSync } = require('child_process');
const path = require('path');

// Core paths
const rootDir = path.join(__dirname, '..');
const pkgPath = path.join(rootDir, 'package.json');

async function main() {
  try {
    // 1. Sync the version from your CLI tool to package.json
    console.log('[1/2] Fetching application version...');
    const version = execSync('dotnet aardpack --parse-only', { encoding: 'utf8' }).trim();

    execSync(`npm version ${version} --no-git-tag-version --allow-same-version`);
    console.log(`Version updated to: ${version}`);

    // 2. Determine target platform/arch from npm script arguments
    // Expecting args format like: node scripts/build.js --platform=win32 --arch=x64
    const args = process.argv.slice(2).reduce((acc, arg) => {
      const [key, val] = arg.replace(/^--/, '').split('=');
      acc[key] = val;
      return acc;
    }, {});

    const platform = args.platform || process.platform; // fallback to host OS
    const arch = args.arch || process.arch;

    console.log(`[2/2] Packaging application for ${platform} (${arch})...`);

    const currentYear = new Date().getFullYear();

    // 3. Invoke @electron/packager programmatically
    const appPaths = await packager({
      dir: rootDir,
      out: path.join(rootDir, 'dist'),
      platform: platform,
      arch: arch,
      overwrite: true,
      icon: path.join(rootDir, 'aardvark'),
      appCopyright: `Copyright (C) ${currentYear} Aardvark Platform Team. All Rights Reserved.`,
      win32metadata: {
        FileDescription: 'Aardium',
        ProductName: 'Aardium',
        OriginalFilename: 'Aardium.exe'
      }
    });

    console.log(`Successfully packaged application at:\n`, appPaths.join('\n'));

  } catch (error) {
    console.error('Build failed:', error);
    process.exit(1);
  }
}

main();