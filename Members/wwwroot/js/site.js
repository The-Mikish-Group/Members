// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

function cleanCurrencyInputOnTheFly(inputElement) {
    let originalValue = inputElement.value;
    let cursorPos = inputElement.selectionStart;
    let charsRemovedBeforeCursor = 0;

    // Count characters to be removed before the cursor
    for (let i = 0; i < cursorPos; i++) {
        if (originalValue[i] === '$' || originalValue[i] === ',') {
            charsRemovedBeforeCursor++;
        }
    }

    let cleanedValue = originalValue.replace(/\$|,/g, '');

    if (cleanedValue !== originalValue) {
        inputElement.value = cleanedValue;
        // Adjust cursor position
        let newCursorPos = cursorPos - charsRemovedBeforeCursor;
        inputElement.setSelectionRange(newCursorPos, newCursorPos);
    }
}

function formatCurrencyInputOnBlur(inputElement) { // Renamed for clarity
    let originalValue = inputElement.value; // Value should be already cleaned of $ and ,
    let cleanedValue = originalValue.trim(); // Trim whitespace
    
    let $input = typeof $ === 'function' ? $(inputElement) : null;

    if (cleanedValue === '') {
        inputElement.value = ''; // Leave empty if it was empty or became empty
    } else {
        let numericValue = parseFloat(cleanedValue);
        if (isNaN(numericValue)) {
            // If not a number (e.g., "abc", or just "." if that's not desired)
            inputElement.value = ''; // Clear the field
        } else {
            // Format to two decimal places.
            inputElement.value = numericValue.toFixed(2);
        }
    }

    // If the value has actually changed after formatting (e.g. "123" -> "123.00")
    // or if it was invalid and now cleared.
    if (inputElement.value !== originalValue) {
        if ($input) {
            $input.trigger('change'); // Trigger change for jQuery validation
        } else if ('createEvent' in document) {
            const evt = document.createEvent('HTMLEvents');
            evt.initEvent('change', false, true);
            inputElement.dispatchEvent(evt);
        }
    }
    
    // Attempt to clear existing validation messages if jQuery validation is present
    // This part is kept from previous attempts; its effectiveness might vary.
    if ($input && $input.closest('form').data('validator')) {
        var validator = $input.closest('form').data('validator');
        validator.errorsFor(inputElement).remove();
        $input.removeClass('is-invalid').removeClass('input-validation-error');
        // We might want to re-validate it silently or let the next event (like submit) do it.
        // $input.valid(); // This would show new errors if any, might be too aggressive for onblur.
    }
}
