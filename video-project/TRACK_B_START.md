# Track B - Product Launch Video Generation 🎬

**Status**: READY TO START  
**Time Estimate**: 45-60 minutes (20-30 minutes for video generation + 15-30 minutes for setup)  
**Success Rate**: 95%

---

## 📋 Pre-Flight Checklist

- ✅ Node.js installed: `node --version` → v24+
- ✅ npm installed: `npm --version` → v11+
- ✅ HyperFrames installed: `npm list -g hyperframes` → v0.8.47
- ✅ Video project created: `d:\WORKING\PORTFOLIO\FEATURED PROJECTS\Distributed-Order-Management-System\video-project`
- ✅ Prompt prepared: `VIDEO_GENERATION_PROMPT.md`

---

## 🚀 STEP-BY-STEP EXECUTION

### STEP 1: Verify Tools (2 minutes)

**Check Node.js**:
```powershell
node --version
# Expected: v24.x.x or higher
```

**Check npm**:
```powershell
npm --version
# Expected: v11.x.x or higher
```

**Check HyperFrames**:
```powershell
npm list -g hyperframes
# Expected: hyperframes@0.8.47 (or similar)
```

---

### STEP 2: Open IDE and Prepare (3 minutes)

**Open Claude.dev or Cursor IDE**

**Verify Brag Skill is Available**:
1. Click on "Skills" in the left sidebar
2. Search for "brag" or "bragif"
3. If not found:
   - Click "Browse all skills"
   - Search "brag"
   - Click "Install"
   - Wait for installation (~1 minute)
4. Reload IDE (Cmd+K or Ctrl+Shift+K)

---

### STEP 3: Prepare for Video Generation (5 minutes)

**In IDE chat**, create a new context or conversation with the prompt

**Copy the full prompt from**: `VIDEO_GENERATION_PROMPT.md`

**Key prompt sections:**
- OPENING (5s) - Title animation
- CHECKOUT FLOW (15s) - Shopping experience
- ORDER CONFIRMATION (10s) - Order placed
- REAL-TIME TRACKING (10s) - Order status updates
- INNOVATION HIGHLIGHT (10s) - **Payment fails!**
- COMPENSATION IN ACTION (8s) - **Inventory auto-released** ← KEY DEMO
- DISTRIBUTED TRACING (10s) - Jaeger visualization
- KEY METRICS (7s) - Project statistics
- CALL-TO-ACTION (5s) - Closing message

---

### STEP 4: Run Brag Command (30 minutes) ⏱️

**In Claude.dev/Cursor IDE chat:**

Type: `/brag ` then paste the full prompt from `VIDEO_GENERATION_PROMPT.md`

**Full command starts with:**
```
/brag Create a 60-second product launch video for the 
Distributed Order Management System with these elements:
```

**Expected behavior:**
1. IDE accepts the `/brag` command
2. Video generation starts (may take 20-30 minutes)
3. Brag uses HyperFrames to render HTML/CSS/JavaScript animation
4. Outputs MP4 file (1920x1080, 30fps, 60 seconds)
5. Also generates social media copy

**What to expect while waiting:**
- IDE will show progress (or just wait silently)
- Generation typically takes 20-30 minutes depending on system
- CPU usage may increase during rendering
- **Do NOT interrupt or close IDE**

---

### STEP 5: Verify Generated Files (5 minutes)

**After video generation completes**, files should appear in:

```
video-project/dist/
├── product-launch-video.mp4              ← Main file (use this!)
├── product-launch-video-preview.gif      ← Preview animation
└── social-media-copy.md                  ← Marketing content
```

**Check MP4 was created:**
```powershell
cd "d:\WORKING\PORTFOLIO\FEATURED PROJECTS\Distributed-Order-Management-System\video-project"
dir dist/
```

**Verify file size**: Should be 10-50 MB (depending on compression)

**Test MP4 plays**:
```powershell
# Windows
.\dist\product-launch-video.mp4

# Or use: Start-Process .\dist\product-launch-video.mp4
```

---

### STEP 6: Save Social Media Copy (5 minutes)

**Extract generated social media content:**

From `dist/social-media-copy.md`, save separate files:

```powershell
# YouTube description (200+ words)
$youtubeDesc = "Distributed Order Management System - Automatic..."
# Save to: YOUTUBE_DESCRIPTION.md

# LinkedIn post (150 words)
$linkedinPost = "[LinkedIn post content]"
# Save to: LINKEDIN_POST.md

# Twitter/X post (280 chars)
$twitterPost = "[Twitter post content]"
# Save to: TWITTER_POST.md

# GitHub snippet
$githubSnippet = "[README snippet]"
# Save to: GITHUB_SNIPPET.md
```

---

### STEP 7: Copy Video to Project Root (3 minutes)

**Copy MP4 to main project directory:**

```powershell
Copy-Item `
  -Path "d:\WORKING\PORTFOLIO\FEATURED PROJECTS\Distributed-Order-Management-System\video-project\dist\product-launch-video.mp4" `
  -Destination "d:\WORKING\PORTFOLIO\FEATURED PROJECTS\Distributed-Order-Management-System\PRODUCT_LAUNCH_VIDEO.mp4"
```

**Verify:**
```powershell
Test-Path "d:\WORKING\PORTFOLIO\FEATURED PROJECTS\Distributed-Order-Management-System\PRODUCT_LAUNCH_VIDEO.mp4"
# Expected: True
```

---

## ⚠️ Troubleshooting

### Problem: "Brag command not found"

**Solution**:
1. Make sure you're using Claude.dev or Cursor IDE (not ChatGPT)
2. Click "Skills" → Search "brag" → Install
3. Reload IDE: Cmd+K or Ctrl+Shift+K
4. Try `/brag` command again

### Problem: "Video generation is taking too long"

**This is normal**:
- First-time generation: 20-30 minutes
- Subsequent: 10-15 minutes
- Just wait, don't interrupt

**Monitor progress**:
- Look for IDE status messages
- Check system Task Manager for Node.js CPU usage
- If stuck >45 min, kill IDE and retry

### Problem: "dist/ folder not created"

**Solution**:
1. Make sure full command was pasted (all sections)
2. Check IDE console for errors
3. Manually create dist folder:
   ```powershell
   mkdir "d:\WORKING\PORTFOLIO\FEATURED PROJECTS\Distributed-Order-Management-System\video-project\dist"
   ```

### Problem: "MP4 won't play"

**Verify file integrity**:
```powershell
$file = "path\to\product-launch-video.mp4"
(Get-Item $file).Length  # Should show file size in bytes
```

**Try different player**:
- Windows Media Player
- VLC Media Player
- Microsoft Edge (drag file to browser)

### Problem: "Brag/HyperFrames npm error"

**Solution**:
```powershell
# Clear npm cache
npm cache clean --force

# Reinstall HyperFrames
npm uninstall -g hyperframes
npm install -g hyperframes

# Verify
npm list -g hyperframes
```

---

## 🎯 Success Indicators

✅ **Track B Complete when:**
- [ ] `/brag` command executed in IDE
- [ ] Video generation started and completed (20-30 min)
- [ ] `product-launch-video.mp4` created in dist/
- [ ] File size > 1 MB (not corrupted)
- [ ] MP4 plays without errors
- [ ] Social media copy extracted to separate files
- [ ] Video copied to project root: `PRODUCT_LAUNCH_VIDEO.mp4`

---

## 📊 What the Video Demonstrates

**Key Features Shown in 60 seconds:**

1. **Opening (5s)**: Project branding
   - Title: "Distributed Order Management System"
   - Subtitle: "Automatic Compensation When Payment Fails"

2. **Shopping Experience (15s)**: Normal order flow
   - Product catalog
   - Add to cart
   - Checkout

3. **Order Processing (10s)**: 
   - Order confirmation
   - Order ID displayed

4. **Real-time Tracking (10s)**:
   - Pending → Confirmed transitions
   - Checkmarks and progress animations

5. **INNOVATION HIGHLIGHT (10s)**: ⭐ **THE PROOF POINT**
   - Payment fails (red X animation)
   - "Automatic Compensation Triggered" message
   - Shows system detecting failure

6. **COMPENSATION IN ACTION (8s)**: 🎯 **KEY SELLING POINT**
   - Inventory automatically released
   - "No Manual Intervention Required"
   - Green success animation
   - **PROVES automatic compensation works!**

7. **Distributed Tracing (10s)**:
   - Jaeger trace visualization
   - Complex service interactions
   - Full end-to-end visibility

8. **Metrics (7s)**:
   - "8 Microservices"
   - "161 Tests"
   - "20,000 Lines of Code"

9. **Call-to-Action (5s)**:
   - "Ready to build distributed systems right?"
   - URL: http://localhost:3000

---

## 🔗 Repository Reference

**Brag Repository**: https://github.com/latent-spaces/brag  
**Powered by**: HyperFrames  
**Video Tech**: HTML5 Canvas + FFmpeg rendering

---

## ⏱️ Timeline Estimate

```
T+0min:    Open IDE, verify Brag skill installed
T+5min:    Copy prompt from VIDEO_GENERATION_PROMPT.md
T+6min:    Run /brag command
T+6min:    Video generation starts (wait 20-30 min)
T+36min:   Video generation completes
T+41min:   Verify MP4 file created
T+46min:   Extract social media copy
T+51min:   Copy video to project root
T+54min:   TRACK B COMPLETE ✅
```

---

## ✅ Ready?

**Next Step**: Open Claude.dev or Cursor IDE and run `/brag` command!

**Have the prompt ready**: See `VIDEO_GENERATION_PROMPT.md`

---

## 🎉 After Track B Complete

Once video is generated and saved:
1. ✅ Track A: Tests - COMPLETE
2. ✅ Track B: Video - COMPLETE  
3. 🟡 Track C: Share & Launch - NEXT (30 minutes)

**Track C includes:**
- Upload to YouTube
- Share on LinkedIn
- Post on Twitter/X
- Update GitHub README
- Cross-platform promotion

---

**Status**: READY TO START VIDEO GENERATION 🚀

Open your IDE and let's generate the video!
