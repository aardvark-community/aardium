const {dialog, Menu} = require('electron').remote

var aardvark = {};
document.aardvark = aardvark;

aardvark.openFileDialog = function(config, callback) {
	if(!callback) callback = config;
	dialog.showOpenDialog({properties: ['openFile', 'multiSelections']}, callback);
};

aardvark.setMenu = function(template) {
	const menu = Menu.buildFromTemplate(template)
	Menu.setApplicationMenu(menu)
};
