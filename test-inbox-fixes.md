# Testing Script for Inbox Window Fixes (Commit 1605)

## Prerequisites
1. Build completed successfully in Debug mode
2. SquadDash running
3. You're in a Squad workspace

## Test 1: Window Size Diagnostic
**Objective:** Verify the window size logging is working

### Steps:
1. Delete the old log file (if it exists):
   ```powershell
   Remove-Item "$env:TEMP\squaddash-window-debug.log" -ErrorAction SilentlyContinue
   ```

2. In SquadDash, trigger an inbox message to open (any method)

3. Check the log file:
   ```powershell
   Get-Content "$env:TEMP\squaddash-window-debug.log"
   ```

### Expected Result:
```
[HH:mm:ss.fff] Constructor: Width=750, Height=550
[HH:mm:ss.fff] Loaded: ActualWidth=750, ActualHeight=550
```

### What to Look For:
- **If Constructor shows 750x550 BUT Loaded shows different values:** Something is resizing the window after construction
- **If both show 750x550 but the window appears smaller:** WPF layout issue (measure/arrange)
- **If Constructor shows different values:** Something is modifying Width/Height before UI construction

## Test 2: Excerpt Selection (The Main Fix)
**Objective:** Verify excerpt text selection now works

### Steps:
1. Delete the old log files:
   ```powershell
   Remove-Item "$env:TEMP\squaddash-excerpt-debug.log" -ErrorAction SilentlyContinue
   Remove-Item "$env:TEMP\squaddash-window-debug.log" -ErrorAction SilentlyContinue
   ```

2. Create a test inbox message with body text:
   - The body must contain multiple paragraphs of text
   - Example: Use the sample message from `excerpt-testing-guide.md`

3. Open the inbox message window

4. Select some text in the body (drag to select)

5. Right-click the selection → "Add to Chat"

6. In the main window, you should see a follow-up attachment: `📎 Excerpt: [Subject]`

7. Click the excerpt attachment

8. **Check the new inbox window that opens:**
   - ✅ The selected text should be HIGHLIGHTED (selection visible)
   - ✅ The window should SCROLL to show the selected text
   - ✅ The text should be immediately visible (not scrolled out of view)

9. Check the excerpt debug log:
   ```powershell
   Get-Content "$env:TEMP\squaddash-excerpt-debug.log"
   ```

### Expected Log Output:
```
[HH:mm:ss.fff] [SelectAndScroll] Called with text: '[your excerpt text]'
[HH:mm:ss.fff] [SelectAndScroll] Document found, searching for text
[HH:mm:ss.fff] [SelectAndScroll] Document text length: [number]
[HH:mm:ss.fff] [SelectAndScroll] First 500 chars: '[document preview]'
[HH:mm:ss.fff] [SelectAndScroll] Looking for excerpt: '[your excerpt]'
[HH:mm:ss.fff] [SelectAndScroll] Excerpt exists in doc: True
[HH:mm:ss.fff] [SelectAndScroll] Text found! Setting selection and scrolling
[HH:mm:ss.fff] [SelectAndScroll] Selection set, checking if selection is visible
[HH:mm:ss.fff] [SelectAndScroll] Selection.IsEmpty: False
[HH:mm:ss.fff] [SelectAndScroll] Selection text: '[your excerpt]'
[HH:mm:ss.fff] [SelectAndScroll] Character rect found, calling BringIntoView on rect
[HH:mm:ss.fff] [SelectAndScroll] Scrolling complete
```

### What to Look For:
- **If log file doesn't exist:** The Dispatcher.BeginInvoke callback still isn't firing (this should be fixed now)
- **If log shows "Excerpt exists in doc: False":** Text extraction or matching issue
- **If log shows "Text NOT found in document":** Search algorithm issue
- **If selection appears but window doesn't scroll:** BringIntoView issue

## Test 3: Excerpt Selection (Edge Cases)

### Test 3a: Multi-paragraph Excerpt
- Select text spanning multiple paragraphs
- Add to chat
- Click excerpt attachment
- Verify entire selection is highlighted

### Test 3b: Excerpt at Start of Document
- Select text from the very beginning
- Add to chat
- Click excerpt attachment
- Verify selection works (no scroll needed)

### Test 3c: Excerpt at End of Document
- Select text from the very end
- Add to chat
- Click excerpt attachment
- Verify window scrolls to bottom and highlights text

## Results Template

Copy this and fill it in:

```
## Test Results - [Your Name] - [Date/Time]

### Test 1: Window Size Diagnostic
- Log file exists: [ ] YES [ ] NO
- Constructor size: Width=___ Height=___
- Loaded size: ActualWidth=___ ActualHeight=___
- Actual window appearance: [ ] Correct (750x550) [ ] Incorrect (___x___)

### Test 2: Excerpt Selection
- Log file exists: [ ] YES [ ] NO
- Text found in document: [ ] YES [ ] NO
- Selection highlighted: [ ] YES [ ] NO
- Window scrolled to selection: [ ] YES [ ] NO

### Test 3: Edge Cases
- Multi-paragraph: [ ] PASS [ ] FAIL
- Start of doc: [ ] PASS [ ] FAIL
- End of doc: [ ] PASS [ ] FAIL

### Issues Found:
[Describe any issues here]

### Log Files:
[Paste relevant log excerpts here]
```

## Quick Test (All-in-One)
Run this PowerShell script to test everything at once:

```powershell
# Clear old logs
Remove-Item "$env:TEMP\squaddash-excerpt-debug.log" -ErrorAction SilentlyContinue
Remove-Item "$env:TEMP\squaddash-window-debug.log" -ErrorAction SilentlyContinue

Write-Host "✅ Old logs cleared. Now test the features in SquadDash." -ForegroundColor Green
Write-Host ""
Write-Host "After testing, run this to see the logs:" -ForegroundColor Yellow
Write-Host ""
Write-Host "Write-Host '=== WINDOW SIZE LOG ===' -ForegroundColor Cyan" -ForegroundColor Gray
Write-Host "Get-Content '$env:TEMP\squaddash-window-debug.log'" -ForegroundColor Gray
Write-Host "Write-Host '' -ForegroundColor Gray"
Write-Host "Write-Host '=== EXCERPT DEBUG LOG ===' -ForegroundColor Cyan" -ForegroundColor Gray
Write-Host "Get-Content '$env:TEMP\squaddash-excerpt-debug.log'" -ForegroundColor Gray
```
