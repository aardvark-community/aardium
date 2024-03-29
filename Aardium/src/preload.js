const electron = require('electron')
const remote = require('@electron/remote')
const shm = require('node-shared-mem')

electron.remote = remote;
var aardvark = {};
document.aardvark = aardvark;
window.aardvark = aardvark;

aardvark.openFileDialog = function(config, callback) {
	if(!callback) callback = config;
	electron.remote.dialog.showOpenDialog({properties: ['openFile', 'multiSelections']}).then(e => callback(e.filePaths));
};

aardvark.moveWindowTop = function () {
	electron.remote.getCurrentWindow().moveTop();
}

aardvark.focusWindow = function () {
	electron.remote.getCurrentWindow().focus();
}

aardvark.setMenu = function(template) {
	const menu = rem.Menu.buildFromTemplate(template)
	electron.remote.Menu.setApplicationMenu(menu)
};

aardvark.openMapping = function (name, len) {
	var mapping = new shm.SharedMemory(name, len);
	var uint8arr = new Uint8Array(mapping.buffer);
	var uint8Clamped = new Uint8ClampedArray(mapping.buffer);

	var result = 
		{ 
			readString: function() {
				if(mapping.buffer) {
					var i = 0;
					var res = "";
					while(uint8arr[i] != 0 && i < mapping.length) {
						res += String.fromCharCode(uint8arr[i]);
						i++;
					}
					return res;
				}
				else return "";
			},

			readImageData: function(sx, sy) {
				return new ImageData(uint8Clamped.slice(0, sx *sy * 4), sx, sy);
			},

			close: function() {
				result.length = 0;
				result.name = "";
				result.buffer = new ArrayBuffer(0);
				result.readString = function() { return ""; };
				result.readImageData = function() { return null; }
				result.close = function() { };
				mapping.close();
			},

			buffer: mapping.buffer,
			length: mapping.length,
			name: mapping.name
		};

	return result;
};


aardvark.dialog = remote.dialog;
aardvark.electron = electron;

aardvark.captureFullscreen = function(path) 
{
	aardvark.electron.remote.getCurrentWindow().capturePage(function (e) 
	{ 
		aardvark.electron.remote.require('fs').writeFile(path, e.toPNG()); 
	});
};