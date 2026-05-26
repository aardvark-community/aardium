const electron = require('electron')
const remote = require('@electron/remote')
const shm = require('node-shared-mem')

electron.remote = remote;
var aardvark = {};
document.aardvark = aardvark;
window.aardvark = aardvark;

aardvark.openFileDialog = function (config, callback) {
	if (!callback) callback = config;
	electron.remote.dialog.showOpenDialog({ properties: ['openFile', 'multiSelections'] }).then(e => callback(e.filePaths));
};

aardvark.moveWindowTop = function () {
	electron.remote.getCurrentWindow().moveTop();
}

aardvark.focusWindow = function () {
	electron.remote.getCurrentWindow().focus();
}

aardvark.setMenu = function (template) {
	const menu = rem.Menu.buildFromTemplate(template)
	electron.remote.Menu.setApplicationMenu(menu)
};

aardvark.openMemoryMapping = function (name, length) {
	return new shm.SharedMemory(name, length);
};

aardvark.dialog = remote.dialog;
aardvark.electron = electron;

aardvark.captureFullscreen = function (path) {
	aardvark.electron.remote.getCurrentWindow().capturePage(function (e) {
		aardvark.electron.remote.require('fs').writeFile(path, e.toPNG());
	});
};