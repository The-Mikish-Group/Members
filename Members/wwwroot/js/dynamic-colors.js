(function () {
    fetch('/api/colors')
        .then(response => response.json())
        .then(colors => {
            const style = document.createElement('style');
            style.innerHTML = `:root {
${Object.entries(colors).map(([key, value]) => `    --${key}: ${value};`).join('\n')}
            }`;
            document.head.appendChild(style);
        });
})();
