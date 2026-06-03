mergeInto(LibraryManager.library, {
    TriggerBrowserFileUpload: function(objectName, methodName) {
        var objName  = UTF8ToString(objectName);
        var methName = UTF8ToString(methodName);

        var input    = document.createElement('input');
        input.type   = 'file';
        input.accept = '.txt';
        input.multiple = true;

        input.onchange = function(e) {
            var files = e.target.files;
            if (!files || files.length === 0) return;

            // Итерируем ВСЕ файлы
            Array.from(files).forEach(function(file) {
                var reader = new FileReader();

                reader.onload = function(event) {
                    var content = event.target.result;
                    var payload = file.name + "|::|" + content;
                    Module.SendMessage(objName, methName, payload);
                };

                reader.readAsText(file);
            });
        };

        input.click();
    }
});