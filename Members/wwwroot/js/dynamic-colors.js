(function () {
    fetch('/api/colors?v=' + new Date().getTime())
        .then(response => response.json())
        .then(colors => {
            let css = ':root {\n';
            for (const [key, value] of Object.entries(colors)) {
                css += `    ${key}: ${value};\n`;
            }
            css += '}';
            const style = document.createElement('style');
            style.innerHTML = css;
            document.head.appendChild(style);
        });
})();
