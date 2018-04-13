const {dialog} = require('electron').remote

var aardvark = {};
document.aardvark = aardvark;

aardvark.openFileDialog = function(config, callback) {
	if(!callback) callback = config;
	dialog.showOpenDialog({properties: ['openFile', 'multiSelections']}, callback);
};