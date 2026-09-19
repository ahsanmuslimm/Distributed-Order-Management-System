# Generate Product Launch Video - Quick Action Guide

**Tool**: Bragif (Claude Code skill with HyperFrames)  
**Time Required**: 1-2 hours total  
**Output**: Professional 60-second MP4 launch video  

---

## TL;DR - Commands to Run

### Quick Setup (5 minutes)

```bash
# Install HyperFrames globally
npm install -g hyperframes

# Create video project
npx hyperframes init order-management-video
cd order-management-video

# Start live preview (keep this running)
npx hyperframes preview &
```

### Generate Video (30 minutes)

**In Claude.dev or Cursor IDE, run**:

```
/brag Create a 60-second product launch video for 
the Distributed Order Management System.

Include:
1. Title: "Distributed Order Management System"
2. Tagline: "Automatic Compensation When Payment Fails"
3. Scene 1 (5s): Title animation with brand colors
4. Scene 2 (10s): Show React UI checkout page - browsing products
5. Scene 3 (10s): Show cart filling up, order total updating
6. Scene 4 (8s): Order confirmation screen with Order ID
7. Scene 5 (10s): Real-time order status page - Pending → Confirmed transition
8. Scene 6 (8s): Highlight "What Makes This Different?" - Payment fails scenario
9. Scene 7 (8s): Show automatic compensation happening (inventory released)
10. Scene 8 (8s): Jaeger distributed trace showing all microservices connected
11. Scene 9 (7s): Key stats animation: 8 microservices, 161 tests, end-to-end tracing
12. Scene 10 (5s): Call-to-action: "Try it now - http://localhost:3000"
13. Background: Upbeat, professional music
14. Colors: Primary #0066cc (blue), Success #00aa44 (green), Text white
15. Format: 1920x1080, 30fps, MP4

Use project-info.json and screenshots from documentation.
Make it shareable - suitable for YouTube, LinkedIn, Twitter.
```

### Export (10 minutes)

```bash
# Video generated to:
# dist/product-launch-video.mp4

# Open and verify it plays:
# dist/product-launch-video.mp4

# Copy to project root for easy access:
cp dist/product-launch-video.mp4 ../../product-launch-video.mp4
```

---

## What You Need to Prepare

### 1. Screenshots (Can Take from Running UI)

Open the React UI and take 4-5 key screenshots:

**Screenshot 1: Checkout Page**
```bash
# Terminal 1: Start services (all 6)
cd src/Orders.Service && dotnet run
# (repeat for other services)

# Terminal 7: Start Gateway
cd src/Gateway && dotnet run

# Terminal 8: Start UI
cd src/UI && npm run dev
```

**Take screenshots of**:
1. Product catalog page (showing products)
2. Cart with items added (showing total)
3. Order confirmation page (after placing order)
4. Order status page (Pending state)
5. Order status page (Confirmed state - after waiting)
6. Jaeger trace screenshot (http://localhost:16686)

**Save to**: `src/UI/screenshots/` directory

### 2. Project Information JSON

Create file: `order-management-video/project-info.json`

```json
{
  "name": "Distributed Order Management System",
  "tagline": "Automatic Compensation When Payment Fails",
  "description": "A distributed microservices system demonstrating saga patterns, automatic compensation, and end-to-end distributed tracing",
  "website": "http://localhost:3000",
  "github": "https://github.com/your-repo",
  "keyFeatures": [
    "8 Microservices Working in Harmony",
    "Automatic Compensation on Payment Failure",
    "End-to-End Distributed Tracing",
    "Real-Time Order Tracking",
    "Production-Ready Code"
  ],
  "stats": {
    "microservices": 8,
    "tests": 161,
    "linesOfCode": 20000,
    "tracing": "OpenTelemetry + Jaeger"
  },
  "colors": {
    "primary": "#0066cc",
    "success": "#00aa44",
    "danger": "#cc3300",
    "background": "#f5f5f5"
  }
}
```

---

## Installation Steps

### Step 1: Install HyperFrames

```bash
# Option A: Global (recommended)
npm install -g hyperframes

# Verify installation
hyperframes --version
```

### Step 2: Create Video Project

```bash
# Create new project
npx hyperframes init order-management-video

# Navigate to it
cd order-management-video

# Structure created:
# order-management-video/
# ├── src/
# │   └── video.tsx          (React component for video)
# ├── public/
# │   └── index.html
# ├── package.json
# └── hyperframes.config.json
```

### Step 3: Start Live Preview

```bash
# In terminal, keep running
npx hyperframes preview

# Output:
# → Preview running at http://localhost:3000
# 
# This shows video preview in real-time
# Refresh browser as you iterate
```

### Step 4: Install Bragif Skill (Claude.dev/Cursor)

**In Claude.dev IDE**:
1. Click "Skills" button in left sidebar
2. Search for "bragif" or "brag"
3. Click "Install"
4. Now you can use `/brag` command

---

## Generate the Video

### Simple Approach (Recommended)

**In Claude.dev IDE, type**:

```
/brag Create a 60-second product launch video for 
the Distributed Order Management System showing the 
order checkout flow, real-time tracking, and 
automatic compensation when payment fails.
```

**Bragif will**:
1. Generate HTML/CSS/JavaScript for video
2. Render to MP4
3. Generate social media copy
4. Return ready-to-share video file

### Detailed Approach (More Control)

**In Claude.dev IDE, type**:

```
/brag Create a professional 60-second launch video 
for "Distributed Order Management System" with 
these scenes:

1. (5s) Title card with blue gradient background
   "Distributed Order Management System"
   Subtitle: "Automatic Compensation When Payment Fails"

2. (12s) Product checkout flow
   Show: Product catalog → Add to cart → Order total updating

3. (10s) Order confirmation
   Show: Order placed successfully, Order ID displayed

4. (10s) Real-time order tracking
   Show: Order status updating from Pending to Confirmed

5. (10s) The innovation
   Highlight: "What happens when payment fails?"
   Answer: "Automatic compensation - no manual intervention"

6. (10s) Distributed tracing
   Show: Jaeger trace with all microservices connected

7. (5s) Key metrics
   Animate: "8 Microservices" "161 Tests" "Full Tracing"

8. (5s) Call-to-action
   "Try it now → http://localhost:3000"

Music: Upbeat, professional electronic
Colors: #0066cc primary, #00aa44 success, white text
Format: 1920x1080, MP4
Duration: 60 seconds

Also generate social media copy for YouTube, LinkedIn, and Twitter.
```

---

## Export & Use Video

### Where Video Gets Saved

After generation, files appear in:

```
order-management-video/dist/
├── product-launch-video.mp4         ← YOUR VIDEO
├── product-launch-video.mov         ← Alternative format
└── product-launch-video-preview.gif ← Preview
```

### Copy to Project Root

```bash
# Make it easy to find
cp dist/product-launch-video.mp4 ../../PRODUCT_LAUNCH_VIDEO.mp4

# Now accessible at:
# /path/to/project/PRODUCT_LAUNCH_VIDEO.mp4
```

### Verify Video Works

```bash
# Play video
open dist/product-launch-video.mp4  # macOS
xdg-open dist/product-launch-video.mp4  # Linux
start dist/product-launch-video.mp4  # Windows
```

---

## Share the Video

### Platform 1: YouTube

1. Go to youtube.com
2. Click "Create" → "Upload video"
3. Select `PRODUCT_LAUNCH_VIDEO.mp4`
4. Title: "Distributed Order Management System - Product Launch"
5. Description: (use generated copy from Bragif)
6. Tags: DistributedSystems, Microservices, OpenSource, OrderManagement
7. Publish

### Platform 2: LinkedIn

1. Go to linkedin.com
2. Click "Start a post"
3. Click "Video" icon
4. Upload `PRODUCT_LAUNCH_VIDEO.mp4`
5. Write post (use generated copy)
6. Tag: #DistributedSystems #OpenSource #Microservices
7. Post

### Platform 3: Twitter/X

1. Go to twitter.com
2. Click "Compose post"
3. Click media icon
4. Upload `PRODUCT_LAUNCH_VIDEO.mp4`
5. Write tweet (use generated copy)
6. Tag: #DistributedSystems #OpenSource
7. Post

### Platform 4: GitHub

1. Update README.md with video link:
```markdown
## 🎬 Product Launch Video

Watch our 60-second launch video to see the system in action:

[![Product Launch Video](https://img.youtube.com/vi/VIDEO_ID/0.jpg)](https://www.youtube.com/watch?v=VIDEO_ID)

[Or download the video](./PRODUCT_LAUNCH_VIDEO.mp4)
```

### Platform 5: Project Website

```html
<video width="100%" controls>
  <source src="PRODUCT_LAUNCH_VIDEO.mp4" type="video/mp4">
  Your browser does not support the video tag.
</video>
```

---

## If Video Needs Adjustments

### Request Changes

**In Claude.dev, type**:

```
/brag Adjust the product launch video:
- Make the compensation explanation clearer
- Add more time showing Jaeger traces
- Emphasize "no manual intervention"
- Add statistics overlay
- Slightly soften the background music
```

**Bragif will regenerate** with your changes.

### Common Adjustments

```
/brag Adjust:
- Make the video 45 seconds instead of 60
- Use different background music (jazz instead of electronic)
- Add text overlays for key points
- Make the Jaeger trace section longer
- Add customer testimonial quote
- Use different color scheme (green instead of blue)
```

---

## Generated Marketing Copy

Bragif generates ready-to-use social media copy:

### YouTube Description
```
A distributed order management system demonstrating 
saga patterns, automatic compensation, and end-to-end 
distributed tracing with OpenTelemetry and Jaeger.

When payment fails, inventory is automatically released 
without manual intervention. Here's how:

[Full description auto-generated]
```

### LinkedIn Post
```
Just launched: Distributed Order Management System

When payment fails, what happens to reserved inventory?
Our answer: Automatic compensation. No manual intervention.

See the demo → [video]

[Full post auto-generated]
```

### Twitter Post
```
Payment fails → Compensation auto-triggers → Inventory 
released. No manual intervention. No data loss.

Saga pattern done right ✅

[video link]

#DistributedSystems #OpenSource
```

---

## Success Checklist

- [ ] HyperFrames installed globally
- [ ] Video project created (`npx hyperframes init`)
- [ ] Live preview running (`npx hyperframes preview`)
- [ ] Bragif skill installed in Claude.dev
- [ ] Screenshots prepared (4-6 key UI screens)
- [ ] `project-info.json` created
- [ ] `/brag` command executed with detailed prompt
- [ ] Video generated successfully
- [ ] MP4 file appears in `dist/` folder
- [ ] Video plays without errors
- [ ] Video copied to project root
- [ ] Social media copy reviewed
- [ ] Video uploaded to YouTube/LinkedIn/Twitter
- [ ] Links added to GitHub README
- [ ] Project announced via email/newsletter

---

## Estimated Timeline

| Step | Time |
|------|------|
| Install HyperFrames | 5 min |
| Create video project | 5 min |
| Take screenshots | 15 min |
| Create project-info.json | 5 min |
| Run `/brag` command | 5 min |
| Video generates | 10 min |
| Export & verify | 5 min |
| Upload to platforms | 15 min |
| **Total** | **~65 min** |

---

## Pro Tips

### Tip 1: Use Live Preview While Generating
Keep http://localhost:3000 open to see video updates in real-time

### Tip 2: Start Simple, Iterate
First run: Basic video  
Second run: Add more details  
Third run: Polish and refine

### Tip 3: Save Multiple Versions
```bash
cp dist/product-launch-video.mp4 v1-basic.mp4
# After adjustments:
cp dist/product-launch-video.mp4 v2-enhanced.mp4
# Keep best version
```

### Tip 4: Test on All Platforms
Before publishing, download and test:
- YouTube preview
- LinkedIn preview
- Twitter preview
- Ensure all work correctly

### Tip 5: Share Generated Copy
Use exact marketing copy from Bragif for consistency across platforms

---

## Next Steps (Phase 11)

After video is generated:

1. ✅ Create product launch video (you are here)
2. 🔵 Announce on all social platforms
3. 🔵 Update GitHub with video link
4. 🔵 Write blog post featuring video
5. 🔵 Send email announcement
6. 🔵 Submit to Product Hunt
7. 🔵 Tag influencers in tech/distributed systems
8. 🔵 Document launch metrics

---

## Support

### Issues?

**Video not generating**:
- Check HyperFrames version: `hyperframes --version`
- Check Bragif skill installed: Look for "/brag" command available
- Check live preview running: http://localhost:3000 should show something

**Video looks bad**:
- Use `/brag` to adjust with more detailed prompt
- Try different music, colors, timing
- Request specific changes: "Slow down the checkout scene"

**Can't find generated file**:
- Check: `order-management-video/dist/product-launch-video.mp4`
- If missing, check Claude output for errors
- Regenerate with simpler prompt

---

## Success!

Once video is generated and uploaded:

✅ You have a professional product launch video  
✅ Ready to share on all platforms  
✅ Marketing copy included  
✅ Shows complete system end-to-end  
✅ Highlights key innovation (automatic compensation)  
✅ Professional production quality  

**Result**: Professional product announcement that takes your project from 95% complete to fully launched! 🎉

---

*Generate your launch video with one /brag command*

