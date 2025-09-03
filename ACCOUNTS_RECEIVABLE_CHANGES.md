# Accounts Receivable System Changes - Implementation Guide

This document details all changes made to the Oaks-Village Accounts Receivable system that need to be replicated in Fish-Smart and HOA-Cloud projects.

## Overview
We enhanced the payment processing system to:
- Auto-select oldest invoice for payment application
- Implement comprehensive overpayment distribution
- Add balance display and quick-fill options
- Fix invoice voiding with proper credit creation
- Create admin utilities for credit management

---

## 1. RecordPayment Page Enhancements

### Files Modified:
- `Members\Areas\Admin\Pages\AccountsReceivable\RecordPayment.cshtml`
- `Members\Areas\Admin\Pages\AccountsReceivable\RecordPayment.cshtml.cs`

### Changes Made:

#### A. UI Simplification - Auto-Invoice Selection
**BEFORE:** Complex invoice selection UI with different views for DataEntry vs Admin/Manager
**AFTER:** Simplified UI that auto-selects oldest invoice for all users

```html
<!-- REMOVE: Complex invoice selection radio buttons -->
<!-- REPLACE WITH: Simple notification message -->
<div class="alert alert-info">
    <strong>Payment will be automatically applied to the oldest invoice first.</strong>
    @if (Model.OpenInvoicesForUser.Count > 1)
    {
        <br />
        <text>If payment amount exceeds the oldest invoice balance, remaining funds will automatically be applied to other open invoices.</text>
    }
</div>
```

#### B. Balance Display Addition
**ADD:** Account summary section showing current balances

```html
<!-- Account Balance Summary -->
<div class="card bg-light mb-3">
    <div class="card-body">
        <h6 class="card-title mb-3">Account Summary</h6>
        <div class="row">
            <div class="col-sm-4 text-center">
                <div class="fs-4 fw-bold text-danger">@Model.TotalDue.ToString("C")</div>
                <small class="text-muted">Total Due</small>
            </div>
            <div class="col-sm-4 text-center">
                <div class="fs-4 fw-bold text-success">@Model.TotalAvailableUserCreditAmount.ToString("C")</div>
                <small class="text-muted">Available Credits</small>
            </div>
            <div class="col-sm-4 text-center">
                @{
                    var netDue = Model.TotalDue - Model.TotalAvailableUserCreditAmount;
                }
                <div class="fs-4 fw-bold @(netDue > 0 ? "text-danger" : "text-success")">
                    @Math.Abs(netDue).ToString("C")
                </div>
                <small class="text-muted">@(netDue > 0 ? "Net Amount Due" : "Net Credit")</small>
            </div>
        </div>
    </div>
</div>
```

#### C. Quick-Fill Payment Buttons
**ADD:** Buttons to auto-populate common payment amounts

```html
@if (Model.TotalDue > 0)
{
    <div class="mt-2 d-flex gap-2 justify-content-center">
        <button type="button" class="btn btn-sm btn-outline-primary" onclick="setPaymentAmount(@Model.TotalDue)">
            Pay Full Balance (@Model.TotalDue.ToString("C"))
        </button>
        @if (Model.OpenInvoicesForUser != null && Model.OpenInvoicesForUser.Any())
        {
            <button type="button" class="btn btn-sm btn-outline-secondary" onclick="setPaymentAmount(@Model.OpenInvoicesForUser.First().AmountRemaining)">
                Pay Oldest Invoice (@Model.OpenInvoicesForUser.First().AmountRemaining.ToString("C"))
            </button>
        }
    </div>
}
```

**ADD:** JavaScript function for quick-fill buttons

```javascript
function setPaymentAmount(amount) {
    var input = document.getElementById('paymentAmountInput');
    if (input) {
        input.value = amount.toFixed(2);
        // Trigger any validation or formatting
        if (typeof cleanCurrencyInputOnTheFly === 'function') {
            cleanCurrencyInputOnTheFly(input);
        }
    }
}
```

### Model Changes:

#### D. Code-Behind Updates
**ADD:** New property for total due calculation

```csharp
[DataType(DataType.Currency)]
public decimal TotalDue { get; set; }
```

**MODIFY:** OnGetAsync to calculate TotalDue

```csharp
// Calculate total due
TotalDue = OpenInvoicesForUser.Sum(i => i.AmountRemaining);
```

**MODIFY:** InputModel validation - Remove Required from SelectedInvoiceID

```csharp
// REMOVE [Required] attribute from SelectedInvoiceID
[Display(Name = "Apply to Invoice")]
public int? SelectedInvoiceID { get; set; }

// ADD [Required] to payment fields
[Required]
[DataType(DataType.Date)]
[Display(Name = "Payment Date")]
public DateTime? PaymentDate { get; set; } = DateTime.Today;

[Required]
[Range(0.01, 1000000.00, ErrorMessage = "Amount must be greater than 0.")]
[DataType(DataType.Currency)]
[Display(Name = "Payment Amount")]
public decimal? Amount { get; set; }

[Required]
[Display(Name = "Payment Method")]
public PaymentMethod? Method { get; set; } = PaymentMethod.Check;
```

#### E. Unified Payment Handler
**REPLACE:** Both `OnPostSimplePaymentAsync` and `OnPostRecordNewPaymentAsync` with single `OnPostAsync`

```csharp
// Unified payment method for all roles - auto-applies to oldest invoice with full overpayment distribution
public async Task<IActionResult> OnPostAsync()
{
    _logger.LogInformation("OnPostAsync called. UserID: {SelectedUserID}, Amount: {Amount}", Input.SelectedUserID, Input.Amount);
    
    // Basic validation
    if (!Input.PaymentDate.HasValue)
        ModelState.AddModelError("Input.PaymentDate", "Payment Date is required.");
    if (!Input.Amount.HasValue || Input.Amount.Value <= 0)
        ModelState.AddModelError("Input.Amount", "Payment Amount is required and must be greater than 0.");
    if (!Input.Method.HasValue)
        ModelState.AddModelError("Input.Method", "Payment Method is required.");
    
    if (!ModelState.IsValid)
    {
        await OnGetAsync(Input.SelectedUserID, ReturnUrl);
        return Page();
    }

    var user = await _userManager.FindByIdAsync(Input.SelectedUserID);
    if (user == null)
    {
        ModelState.AddModelError(string.Empty, "User not found.");
        await OnGetAsync(Input.SelectedUserID, ReturnUrl);
        return Page();
    }

    // Get the oldest unpaid invoice automatically (ordered by InvoiceDate, then by InvoiceID for tie-breaking)
    var oldestInvoice = await _context.Invoices
        .Where(i => i.UserID == Input.SelectedUserID &&
                   i.Status != InvoiceStatus.Cancelled &&
                   i.Status != InvoiceStatus.Draft &&
                   i.AmountPaid < i.AmountDue)
        .OrderBy(i => i.InvoiceDate)
        .ThenBy(i => i.InvoiceID)
        .FirstOrDefaultAsync();

    if (oldestInvoice == null)
    {
        TempData["StatusMessage"] = "No open invoices found for this member. Payment could not be processed.";
        return RedirectToPage(new { userId = Input.SelectedUserID, returnUrl = ReturnUrl });
    }

    // Auto-assign the oldest invoice and process payment
    Input.SelectedInvoiceID = oldestInvoice.InvoiceID;

    // Use the existing comprehensive payment processing logic
    return await ProcessPaymentWithOverpaymentDistribution();
}
```

**ADD:** Comprehensive payment processing with overpayment distribution

```csharp
// Shared payment processing logic with full overpayment distribution
private async Task<IActionResult> ProcessPaymentWithOverpaymentDistribution()
{
    var invoiceToPay = await _context.Invoices.FirstOrDefaultAsync(i => i.InvoiceID == Input.SelectedInvoiceID.GetValueOrDefault() && i.UserID == Input.SelectedUserID);
    if (invoiceToPay == null)
    {
        _logger.LogWarning("Selected invoice ID {SelectedInvoiceID} not found for user {SelectedUserID}.", Input.SelectedInvoiceID, Input.SelectedUserID);
        ModelState.AddModelError("Input.SelectedInvoiceID", "The selected invoice was not found or does not belong to this user.");
        await OnGetAsync(Input.SelectedUserID, ReturnUrl);
        return Page();
    }

    // Validation checks for invoice status
    if (invoiceToPay.Status == InvoiceStatus.Draft)
    {
        ModelState.AddModelError("Input.SelectedInvoiceID", $"Payments cannot be recorded for Draft invoices (INV-{invoiceToPay.InvoiceID}). Please finalize the invoice first.");
        await OnGetAsync(Input.SelectedUserID, ReturnUrl);
        return Page();
    }

    if (invoiceToPay.Status == InvoiceStatus.Cancelled || invoiceToPay.Status == InvoiceStatus.Paid)
    {
        ModelState.AddModelError("Input.SelectedInvoiceID", $"Invoice {invoiceToPay.InvoiceID} is already {invoiceToPay.Status} and cannot receive further payments.");
        await OnGetAsync(Input.SelectedUserID, ReturnUrl);
        return Page();
    }

    decimal amountToApplyToInvoice = Input.Amount!.Value;
    decimal overpaymentAmount = 0;
    decimal amountRemainingOnInvoice = invoiceToPay.AmountDue - invoiceToPay.AmountPaid;
    
    if (Input.Amount!.Value > amountRemainingOnInvoice)
    {
        amountToApplyToInvoice = amountRemainingOnInvoice;
        overpaymentAmount = Input.Amount.Value - amountRemainingOnInvoice;
        _logger.LogInformation("Overpayment detected. Payment: {PaymentAmount}, Remaining on Invoice {InvoiceID}: {RemainingAmount}. Overpayment: {OverpaymentAmount}",
            Input.Amount, invoiceToPay.InvoiceID, amountRemainingOnInvoice, overpaymentAmount);
    }

    // Create the Payment record for the full amount received
    var payment = new Payment
    {
        UserID = Input.SelectedUserID,
        InvoiceID = invoiceToPay.InvoiceID,
        PaymentDate = Input.PaymentDate!.Value,
        Amount = Input.Amount!.Value,
        Method = Input.Method!.Value,
        ReferenceNumber = string.IsNullOrEmpty(Input.ReferenceNumber) ? null : Input.ReferenceNumber.Length > 100 ? Input.ReferenceNumber[..100] : Input.ReferenceNumber,
        Notes = string.IsNullOrEmpty(Input.Notes) ? null : Input.Notes.Length > 1000 ? Input.Notes[..1000] : Input.Notes,
        DateRecorded = DateTime.UtcNow
    };
    _context.Payments.Add(payment);

    // Update the selected invoice
    invoiceToPay.AmountPaid += amountToApplyToInvoice;
    invoiceToPay.LastUpdated = DateTime.UtcNow;
    if (invoiceToPay.AmountPaid >= invoiceToPay.AmountDue)
    {
        invoiceToPay.Status = InvoiceStatus.Paid;
        invoiceToPay.AmountPaid = invoiceToPay.AmountDue;
    }
    else if (invoiceToPay.DueDate < DateTime.Today.AddDays(-1) && invoiceToPay.Status == InvoiceStatus.Due)
    {
        invoiceToPay.Status = InvoiceStatus.Overdue;
    }

    UserCredit? overpaymentEventCredit = null;
    List<CreditApplication> newCreditApplications = [];

    _context.Invoices.Update(invoiceToPay);

    try
    {
        await _context.SaveChangesAsync();
        _logger.LogInformation("Payment {PaymentId} and primary Invoice {InvoiceId} saved. Overpayment amount: {OverpaymentAmount}",
            payment.PaymentID, invoiceToPay.InvoiceID, overpaymentAmount);

        if (overpaymentAmount > 0)
        {
            _logger.LogInformation("Overpayment of {OverpaymentAmount} occurred for User {UserId} from Payment {PaymentId}. Will create a UserCredit and attempt to apply to other due invoices.",
                overpaymentAmount, Input.SelectedUserID, payment.PaymentID);

            overpaymentEventCredit = new UserCredit
            {
                UserID = Input.SelectedUserID,
                CreditDate = Input.PaymentDate!.Value,
                Amount = overpaymentAmount,
                SourcePaymentID = payment.PaymentID,
                Reason = $"Overpayment from Payment P{payment.PaymentID} on Invoice INV-{invoiceToPay.InvoiceID:D5}.",
                IsApplied = false,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
            _context.UserCredits.Add(overpaymentEventCredit);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Created UserCredit {UserCreditId} for overpayment amount {OverpaymentAmount}.",
                overpaymentEventCredit.UserCreditID, overpaymentAmount);

            var otherDueInvoices = await _context.Invoices
                .Where(i => i.UserID == Input.SelectedUserID &&
                            i.InvoiceID != invoiceToPay.InvoiceID &&
                            (i.Status == InvoiceStatus.Due || i.Status == InvoiceStatus.Overdue) &&
                            i.AmountPaid < i.AmountDue)
                .OrderBy(i => i.DueDate)
                .ToListAsync();

            if (otherDueInvoices.Count != 0)
            {
                _logger.LogInformation("Found {OtherInvoiceCount} other due/overdue invoices for User {UserId} to apply overpayment from UserCredit {UserCreditId}.",
                    otherDueInvoices.Count, Input.SelectedUserID, overpaymentEventCredit.UserCreditID);

                foreach (var otherInvoice in otherDueInvoices)
                {
                    if (overpaymentEventCredit.Amount <= 0) break;

                    decimal amountNeededForOtherInvoice = otherInvoice.AmountDue - otherInvoice.AmountPaid;
                    decimal amountToApplyToOtherInvoice = Math.Min(overpaymentEventCredit.Amount, amountNeededForOtherInvoice);

                    if (amountToApplyToOtherInvoice > 0)
                    {
                        otherInvoice.AmountPaid += amountToApplyToOtherInvoice;
                        otherInvoice.LastUpdated = DateTime.UtcNow;
                        if (otherInvoice.AmountPaid >= otherInvoice.AmountDue)
                        {
                            otherInvoice.Status = InvoiceStatus.Paid;
                            otherInvoice.AmountPaid = otherInvoice.AmountDue;
                        }
                        else if (otherInvoice.DueDate < DateTime.Today.AddDays(-1) && otherInvoice.Status == InvoiceStatus.Due)
                        {
                            otherInvoice.Status = InvoiceStatus.Overdue;
                        }
                        _context.Invoices.Update(otherInvoice);

                        var creditApplication = new CreditApplication
                        {
                            UserCreditID = overpaymentEventCredit.UserCreditID,
                            InvoiceID = otherInvoice.InvoiceID,
                            AmountApplied = amountToApplyToOtherInvoice,
                            ApplicationDate = DateTime.UtcNow,
                            Notes = $"Applied from overpayment (Payment P{payment.PaymentID}, UserCredit UC{overpaymentEventCredit.UserCreditID})"
                        };
                        newCreditApplications.Add(creditApplication);
                        _context.CreditApplications.Add(creditApplication);

                        overpaymentEventCredit.Amount -= amountToApplyToOtherInvoice;
                        payment.Notes = (payment.Notes ?? "") + $" Applied ${amountToApplyToOtherInvoice:F2} from UC{overpaymentEventCredit.UserCreditID} to INV-{otherInvoice.InvoiceID:D5}.";
                        _logger.LogInformation("Recorded CreditApplication: {AmountApplied} from UserCredit {UserCreditId} to Invoice {OtherInvoiceId}. UserCredit remaining: {UserCreditRemaining}",
                            amountToApplyToOtherInvoice, overpaymentEventCredit.UserCreditID, otherInvoice.InvoiceID, overpaymentEventCredit.Amount);
                    }
                }
            }
            else
            {
                _logger.LogInformation("No other due/overdue invoices found for User {UserId} to apply overpayment from UserCredit {UserCreditId}.",
                    Input.SelectedUserID, overpaymentEventCredit.UserCreditID);
            }

            // Update the overpaymentEventCredit state after applications
            if (overpaymentEventCredit.Amount <= 0)
            {
                overpaymentEventCredit.IsApplied = true;
                overpaymentEventCredit.Amount = 0;
                overpaymentEventCredit.AppliedDate = DateTime.UtcNow;
                _logger.LogInformation("UserCredit {UserCreditId} fully consumed by applying to other invoices.", overpaymentEventCredit.UserCreditID);
            }
            overpaymentEventCredit.LastUpdated = DateTime.UtcNow;
            _context.UserCredits.Update(overpaymentEventCredit);
        }

        // Final truncation safeguard for payment notes
        const int dbColumnMaxLength = 1000;
        if (payment.Notes != null && payment.Notes.Length > dbColumnMaxLength)
        {
            _logger.LogWarning("Payment.Notes for UserID {UserId} (PaymentID {PaymentId}) was truncated from {OriginalLength} to {MaxLength} characters.",
                payment.UserID, payment.PaymentID, payment.Notes.Length, dbColumnMaxLength);
            payment.Notes = payment.Notes[..dbColumnMaxLength];
        }
        _context.Payments.Update(payment);

        await _context.SaveChangesAsync();
        _logger.LogInformation("Successfully saved credit applications and updated overpayment UserCredit {UserCreditId}.", overpaymentEventCredit?.UserCreditID);

        // Build Status Message
        var statusMessageBuilder = new System.Text.StringBuilder();
        statusMessageBuilder.Append($"Payment of {Input.Amount!.Value:C} (P{payment.PaymentID}) processed for Invoice INV-{invoiceToPay.InvoiceID:D5} (Status: {invoiceToPay.Status}).");

        if (overpaymentEventCredit != null)
        {
            decimal totalAppliedToOthersFromOverpayment = overpaymentAmount - overpaymentEventCredit.Amount;
            if (totalAppliedToOthersFromOverpayment > 0)
            {
                statusMessageBuilder.Append($" {totalAppliedToOthersFromOverpayment:C} of the overpayment (from UC{overpaymentEventCredit.UserCreditID}) was automatically applied to other due invoices.");
            }

            if (overpaymentEventCredit.Amount > 0)
            {
                statusMessageBuilder.Append($" Remaining overpayment of ${overpaymentEventCredit.Amount:C} credited to account (UC{overpaymentEventCredit.UserCreditID}).");
            }
            else if (overpaymentAmount > 0 && overpaymentEventCredit.Amount <= 0)
            {
                if (totalAppliedToOthersFromOverpayment == 0 && overpaymentAmount > 0)
                {
                    statusMessageBuilder.Append($" The overpayment (UC{overpaymentEventCredit.UserCreditID}) was fully utilized.");
                }
            }
        }
        TempData["StatusMessage"] = statusMessageBuilder.ToString();
        _logger.LogInformation("Successfully processed payment ID {PaymentID}. Final status message: {StatusMessage}",
            payment.PaymentID, TempData["StatusMessage"]);
    }
    catch (DbUpdateException ex)
    {
        _logger.LogError(ex, "DbUpdateException saving payment data. User: {UserId}, Invoice: {InvoiceId}, Amount: {Amount}, PaymentDate: {PaymentDate}, Method: {Method}. Inner Exception: {InnerException}", 
            Input.SelectedUserID, Input.SelectedInvoiceID, Input.Amount, Input.PaymentDate, Input.Method, ex.InnerException?.Message);
        ModelState.AddModelError(string.Empty, $"Database error occurred while saving payment data: {ex.InnerException?.Message ?? ex.Message}");
        await OnGetAsync(Input.SelectedUserID, ReturnUrl);
        return Page();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error saving payment data. User: {UserId}, Invoice: {InvoiceId}, Amount: {Amount}", 
            Input.SelectedUserID, Input.SelectedInvoiceID, Input.Amount);
        ModelState.AddModelError(string.Empty, $"An unexpected error occurred: {ex.Message}");
        await OnGetAsync(Input.SelectedUserID, ReturnUrl);
        return Page();
    }

    if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
    {
        return Redirect(ReturnUrl);
    }
    return RedirectToPage(new { userId = Input.SelectedUserID, returnUrl = ReturnUrl });
}
```

---

## 2. Apply Credits Utility

### Files Created:
- `Members\Areas\Admin\Pages\AccountsReceivable\ApplyCredits.cshtml`
- `Members\Areas\Admin\Pages\AccountsReceivable\ApplyCredits.cshtml.cs`

### Purpose:
Administrative utility to apply all available credits to open invoices for a specific user.

### Key Features:
- Visual balance display (before/after)
- Preview of credit application before execution
- Detailed results showing which credits applied to which invoices
- Comprehensive logging and audit trail

### Navigation Addition:
**Management → Admin Tools → Apply Credits Utility**

---

## 3. MyBilling Page Fixes

### File Modified:
- `Members\Areas\Member\Pages\MyBilling.cshtml`
- `Members\Areas\Member\Pages\MyBilling.cshtml.cs`

### Changes Made:

#### A. Invoice Voiding Fix
**ISSUE:** Admin users couldn't void invoices when viewing another user's billing page
**FIX:** Updated void handler to properly handle ViewedUserId parameter

```csharp
// MODIFY: OnPostVoidInvoiceAsync method
public async Task<IActionResult> OnPostVoidInvoiceAsync(int invoiceId, string voidReason)
{
    // Enhanced logging
    _logger.LogInformation("OnPostVoidInvoiceAsync called for InvoiceID: {invoiceId} by User: {User.Identity?.Name}. ViewedUserId: {ViewedUserId}. Reason: {voidReason}", 
        invoiceId, User.Identity?.Name, ViewedUserId, voidReason);
    
    // ... authorization checks ...
    
    // CHANGE: Find invoice by ID first, then determine correct UserID
    var invoiceToVoid = await _context.Invoices
        .FirstOrDefaultAsync(i => i.InvoiceID == invoiceId);
    
    if (invoiceToVoid == null)
    {
        _logger.LogWarning("Invoice {InvoiceId} not found.", invoiceId);
        TempData["ErrorMessage"] = $"Invoice INV-{invoiceId:D5} not found.";
        return RedirectToPage(new { userId = ViewedUserId ?? "", returnUrl = BackToEditUserUrl });
    }
    
    // Log for debugging
    _logger.LogInformation("Found invoice {InvoiceId} for UserID: {InvoiceUserId}. ViewedUserId from form: {ViewedUserId}", 
        invoiceId, invoiceToVoid.UserID, ViewedUserId);
    
    // For Admin/Manager, use the invoice's actual UserID if ViewedUserId is not provided
    var targetUserId = !string.IsNullOrEmpty(ViewedUserId) ? ViewedUserId : invoiceToVoid.UserID;
    
    // ... rest of void processing logic ...
    
    return RedirectToPage(new { userId = targetUserId, returnUrl = BackToEditUserUrl });
}
```

#### B. Form ID Fixes for JavaScript
**ISSUE:** Void buttons weren't working because forms lacked ID attributes
**FIX:** Added missing ID attributes to void forms

```html
<!-- INVOICE VOID FORM - ADD id attribute -->
<form method="post" asp-page-handler="VoidInvoice" style="display:inline;"
      id="voidInvoiceForm_@trans.InvoiceID.Value.ToString()"
      onsubmit="return handleVoidInvoice('@trans.InvoiceID.Value.ToString()');">
    <!-- form content -->
</form>

<!-- PAYMENT VOID FORM - ADD id attribute -->
<form method="post" asp-page-handler="VoidPayment" style="display:inline;"
      id="voidPaymentForm_@trans.PaymentID.Value.ToString()"
      onsubmit="return handleVoidPayment('@trans.PaymentID.Value.ToString()', '@trans.Description');">
    <!-- form content -->
</form>
```

---

## 4. Navigation Changes

### File Modified:
- `Members\Views\Shared\_PartialHeader.cshtml`

### Changes Made:

#### A. Admin Tools Menu Creation
**ADD:** New Admin Tools submenu under Management

```html
<!-- Admin Tools Dropdown -->
<li class="nav-item dropdown">
    <!-- Admin Tools Toggle -->
    <a class="nav-link dropdown-toggle rounded-2 text-dark ps-2 mb-1" style="width:175px;" href="#" id="adminToolsDropdown" role="button" data-bs-toggle="dropdown" aria-expanded="false" data-bs-auto-close="outside">
        <i class="bi bi-tools"></i> Admin Tools <span class="caret"></span>
    </a>

    <!-- Admin Tools -->
    <ul class="dropdown-menu navbar-header-dropdown-bg navbar-header-dropdown-border ps-2" style="margin-left: 155px; width: 230px;" aria-labelledby="adminToolsDropdown" data-bs-auto-close="outside">
        <li>
            <a class="nav-link text-dark rounded-2 mb-1 ps-2" style="width:200px;" asp-area="Admin" asp-page="/AccountsReceivable/ApplyCredits">
                <i class="bi bi-arrow-repeat"></i> Apply Credits Utility
            </a>
        </li>
        <li>
            <a class="nav-link text-dark rounded-2 mb-1 ps-2" style="width:200px;" asp-area="Identity" asp-page="/AdminFiles">
                <i class="bi bi-lock-fill"></i> Delete Protected Files
            </a>
        </li>
    </ul>
</li>
```

#### B. Menu Item Reorganization
**MOVED:** "Delete Protected Files" from Confidential PDF Documents to Admin Tools

---

## 5. CSS/UI Enhancements

### File Modified:
- `Members\Views\Links\MoreLinks.cshtml`

### Changes Made:
**FIX:** Added bottom margin to prevent footer overlap

```html
<!-- CHANGE: Add mb-4 class -->
<div class="container-fluid mt-4 mb-4">
```

---

## 6. Role-Based Access (DataEntry Role)

### Implementation Notes:
- DataEntry role users see simplified payment interface
- All payment processing logic is the same regardless of role
- DataEntry users cannot access invoice selection (auto-selects oldest)
- Admin/Manager users can access all features including credit utilities

### Role Checks Required:
```csharp
// In controllers/pages that need DataEntry access
[Authorize(Roles = "Admin,Manager,DataEntry")]

// In views for DataEntry-specific UI
@if (User.IsInRole("DataEntry"))
{
    <!-- Simplified UI -->
}
else if (User.IsInRole("Admin") || User.IsInRole("Manager"))
{
    <!-- Full-featured UI -->
}
```

---

## 7. Database Requirements

### Tables Used:
- **Invoices** - Invoice management
- **Payments** - Payment records
- **UserCredits** - Credit management
- **CreditApplications** - Credit application tracking
- **UserProfile** - User information display

### Key Fields:
- **Invoices.Status** - Must support: Draft, Due, Overdue, Paid, Cancelled
- **Payments.SourcePaymentID** - Links credits to originating payments
- **UserCredits.IsApplied** - Tracks credit usage status
- **CreditApplications.IsReversed** - Supports void reversals

---

## 8. Testing Scenarios

### Critical Test Cases:
1. **Simple Payment** - Amount equals invoice amount exactly
2. **Partial Payment** - Amount less than invoice amount
3. **Overpayment - Single Invoice** - Amount exceeds invoice, no other open invoices
4. **Overpayment - Multiple Invoices** - Amount exceeds invoice, multiple open invoices exist
5. **Full Balance Payment** - Amount equals total of all open invoices
6. **Void Paid Invoice** - Void invoice that was paid, verify credit creation
7. **Void Partially Paid Invoice** - Void invoice with partial payment
8. **Apply Credits Utility** - Use utility to apply existing credits to invoices

### Role Testing:
- **Admin** - Access all features, view other users' billing
- **Manager** - Access all features, view other users' billing
- **DataEntry** - Simplified interface, auto-invoice selection
- **Member** - View own billing only

---

## Implementation Priority Order

1. **RecordPayment Page** - Core payment processing
2. **MyBilling Fixes** - Void functionality
3. **Navigation Updates** - Admin Tools menu
4. **Apply Credits Utility** - Administrative tool
5. **Role Setup** - Add DataEntry role to target systems
6. **Testing** - Comprehensive payment scenarios

---

## Deployment Checklist

### Before Deployment:
- [ ] Backup database
- [ ] Test payment processing in staging
- [ ] Verify role permissions
- [ ] Test void functionality
- [ ] Confirm navigation works

### After Deployment:
- [ ] Test one payment manually
- [ ] Verify void creates proper credits  
- [ ] Check admin utilities work
- [ ] Confirm overpayment distribution
- [ ] Monitor logs for errors

---

This comprehensive documentation covers all changes needed to replicate the Oaks-Village Accounts Receivable enhancements in Fish-Smart and HOA-Cloud projects.