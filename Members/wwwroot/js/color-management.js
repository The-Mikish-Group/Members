(function () {
    fetch('/api/colors')
        .then(response => response.json())
        .then(colors => {
            for (const [key, value] of Object.entries(colors)) {
                const input = document.querySelector(`input[name="colors[${key}]"]`);
                if (input) {
                    input.value = value;
                }
            }
        });
})();
