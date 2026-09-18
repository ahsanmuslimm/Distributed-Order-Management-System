# Product Launch Video Guide - Bragif/HyperFrames

**Tool**: Bragif (Claude Code skill powered by HyperFrames)  
**Purpose**: Generate professional product launch video for Distributed Order Management System  
**Duration**: 45-60 seconds  
**Output**: MP4 video file ready to share  

---

## What is Bragif/HyperFrames?

### Overview
Bragif is a Claude Code skill that turns your project into a shareable launch video with one command.

**Powered by HyperFrames**: 
- Open-source tool from HeyGen
- AI agents compose videos using HTML, CSS, and JavaScript
- Renders to MP4, MOV, or WebM
- No timeline or effects panel needed
- "Edit videos by vibe-coding"

**Key Features**:
- ✅ Music included
- ✅ Motion graphics
- ✅ Share copy (social media text)
- ✅ One command to generate

---

## Installation Setup

### Step 1: Install HyperFrames Locally

```bash
# Global installation
npm install -g hyperframes

# OR in project directory
cd /path/to/project
npm install hyperframes
```

### Step 2: Initialize HyperFrames Project

```bash
# Create new HyperFrames project
npx hyperframes init order-management-video

# Navigate to project
cd order-management-video
```

### Step 3: Start Live Preview

```bash
# In separate terminal, keep running
npx hyperframes preview

# Outputs:
# → Local preview running at http://localhost:3000
# This allows real-time video preview as you iterate
```

### Step 4: Install Bragif Claude Code Skill

**Option A: Using Claude.dev**
```
In Claude.dev IDE:
1. Click "Skills" in left sidebar
2. Search for "bragif" or "brag"
3. Click "Install"
4. Ready to use /brag command
```

**Option B: Manual Installation**
```bash
# Clone bragif repository
git clone https://github.com/latent-spaces/brag.git
cd brag

# Install dependencies
npm install

# Link as Claude Code skill
# (Follow official bragif documentation)
```

---

## Video Generation Workflow

### Step 1: Prepare Project Information

Create a file `project-info.json` in your project directory:

```json
{
  "name": "Distributed Order Management System",
  "tagline": "Automatic compensation when things fail",
  "description": "A distributed order management system demonstrating saga patterns, automatic compensation, and end-to-end distributed tracing",
  "keyFeatures": [
    "8 microservices working in harmony",
    "Automatic compensation on payment failure",
    "Full distributed tracing with Jaeger",
    "Real-time order tracking",
    "React UI for customer interactions"
  ],
  "demoUrl": "http://localhost:3000",
  "colors": {
    "primary": "#0066cc",
    "success": "#00aa44",
    "danger": "#cc3300",
    "background": "#f5f5f5"
  },
  "screenshots": [
    {
      "path": "./screenshots/checkout.png",
      "caption": "Browse products and place orders"
    },
    {
      "path": "./screenshots/status.png",
      "caption": "Real-time order tracking"
    },
    {
      "path": "./screenshots/jaeger.png",
      "caption": "Full distributed tracing visibility"
    }
  ]
}
```

### Step 2: Capture Screenshots

**For Demo UI**:
```bash
# Open React UI
cd src/UI && npm run dev
# Opens http://localhost:3000

# Take screenshots:
1. Checkout page (products, cart, checkout button)
2. Order confirmation page
3. Order status page (Pending state)
4. Order status page (Confirmed state)
5. Jaeger trace screenshot
```

**Save to**: `./screenshots/` directory

### Step 3: Use Bragif Command

In Claude.dev or Cursor IDE:

```
/brag Create a 60-second product launch video for the Distributed Order Management System. 

The video should:
1. Start with title: "Distributed Order Management System"
2. Show checkout flow (browsing products, adding to cart, placing order)
3. Highlight: "Automatic Compensation When Payment Fails"
4. Show order tracking in real-time
5. Display Jaeger distributed trace showing all services
6. Emphasize: "8 microservices working seamlessly"
7. Key achievement: Payment failure automatically triggers compensation
8. End with call-to-action: "Try it at http://localhost:3000"
9. Include background music (upbeat, professional)
10. Use brand colors: Primary blue #0066cc, Success green #00aa44
11. Duration: 60 seconds
12. Format: 1920x1080 (Full HD)
13. Output: MP4 format

Project info available in project-info.json and screenshots/ directory.
```

---

## Video Storyboard (What Gets Generated)

### Scene 1: Title & Hook (0-5 seconds)
```
Text: "Distributed Order Management System"
Subtitle: "Automatic Compensation When Things Fail"
Background: Gradient (blue to purple)
Music: Fade in (upbeat electronic)
Motion: Smooth zoom/pan effect
```

### Scene 2: Problem Statement (5-12 seconds)
```
Text: "What happens when payment fails in distributed systems?"
Visual: Question mark animation, shaking elements
Music: Continues, slight tension build
```

### Scene 3: Solution Demo (12-35 seconds)
```
Part A (12-20): Checkout Flow
  - Screenshot of product catalog
  - Animation: Adding items to cart
  - Total updating in real-time
  - "Place Order" button highlight

Part B (20-28): Order Confirmation
  - "Order Placed" confirmation screen
  - Order ID displayed
  - Trace ID linking to Jaeger

Part C (28-35): Status Tracking
  - Real-time status updates
  - Pending → Confirmed transition
  - "Order confirmed" celebration animation
```

### Scene 4: The Key Achievement (35-45 seconds)
```
Title: "What Makes This Different?"
Visual: Payment failure scenario animation
Text animations:
  1. "Payment fails" (red X)
  2. "Compensation auto-triggers" (green check)
  3. "Inventory released automatically" (checkmark)
  4. "No manual intervention needed" (highlight)

Jaeger trace shown with:
  - All services connected
  - Single trace ID flowing through
  - Compensation chain visible
```

### Scene 5: Features & Stats (45-55 seconds)
```
Animated counters:
  - "8 Microservices" (with icons)
  - "161 Tests" (with checkmark)
  - "End-to-End Tracing" (trace visualization)
  - "Real-Time Updates" (clock icon)

Music: Building to climax
```

### Scene 6: Call-to-Action (55-60 seconds)
```
Text: "Ready to see distributed transactions done right?"
Subtext: "http://localhost:3000"
Visual: Fade to clean background with logo
Music: Reaches crescendo, fade out
Text: "Try it now • GitHub • Documentation"
```

---

## Running the Generation

### Method 1: Using Claude.dev IDE

```bash
# 1. Open Claude.dev or Cursor IDE
# 2. Install bragif skill (if not already installed)
# 3. Type in chat:

/brag Generate a 60-second launch video for the Distributed 
Order Management System using the project-info.json and 
screenshots in the project directory.
```

### Method 2: Using HyperFrames CLI Directly

```bash
# Generate video using HyperFrames
npx hyperframes build \
  --title "Distributed Order Management System" \
  --duration 60 \
  --format mp4 \
  --width 1920 \
  --height 1080 \
  --music upbeat \
  --style professional
```

### Method 3: Custom Hyperframes Config

Create `hyperframes.config.json`:

```json
{
  "title": "Distributed Order Management System",
  "duration": 60,
  "format": "mp4",
  "resolution": {
    "width": 1920,
    "height": 1080
  },
  "framerate": 30,
  "musicStyle": "upbeat-tech",
  "colors": {
    "primary": "#0066cc",
    "accent": "#00aa44",
    "background": "#f5f5f5"
  },
  "scenes": [
    {
      "type": "title",
      "text": "Distributed Order Management System",
      "duration": 5
    },
    {
      "type": "screenshot",
      "path": "screenshots/checkout.png",
      "duration": 8
    },
    {
      "type": "screenshot",
      "path": "screenshots/status.png",
      "duration": 8
    },
    {
      "type": "screenshot",
      "path": "screenshots/jaeger.png",
      "duration": 10
    },
    {
      "type": "text-animation",
      "text": "Automatic Compensation",
      "duration": 10
    },
    {
      "type": "cta",
      "text": "http://localhost:3000",
      "duration": 5
    }
  ]
}
```

Run:
```bash
npx hyperframes build --config hyperframes.config.json
```

---

## Output & Results

### Generated Files

After running the command, HyperFrames generates:

```
order-management-video/
├── dist/
│   ├── product-launch-video.mp4          ← Main video file
│   ├── product-launch-video.mov          ← Alternative format
│   └── product-launch-video-preview.gif  ← Preview GIF
│
├── src/
│   ├── video.tsx                         ← React component code
│   ├── styles.css                        ← Generated styles
│   └── assets/
│       ├── music.mp3                     ← Background music
│       └── screenshots/                  ← Embedded images
│
└── export-settings.json                  ← Export metadata
```

### Video Specifications

**Technical Details**:
- Format: MP4 (H.264 codec)
- Resolution: 1920×1080 (Full HD)
- Frame Rate: 30 fps
- Duration: 60 seconds
- File Size: ~15-25 MB
- Color Space: Rec.709
- Audio: AAC stereo, 128 kbps

**Ready For**:
- YouTube upload
- LinkedIn sharing
- Twitter/X posting
- Email marketing
- Website embedding

---

## Generated Marketing Copy

Bragif also generates social media copy:

```
🚀 LAUNCH ALERT

Distributed Order Management System

When payment fails in distributed systems, 
what happens to reserved inventory?

In our system: Automatic compensation. 
No manual intervention. No data loss.

✅ 8 microservices working in harmony
✅ Automatic compensation on failure
✅ End-to-end distributed tracing
✅ Real-time order tracking

See it in action → [link to video]

#DistributedSystems #Microservices #SagaPattern 
#OpenSource #ProductLaunch
```

---

## Step-by-Step Execution

### Day 1: Setup (30 minutes)

```bash
# 1. Install HyperFrames
npm install -g hyperframes

# 2. Create project
npx hyperframes init order-management-video
cd order-management-video

# 3. Start preview
npx hyperframes preview &
# Runs in background on http://localhost:3000

# 4. Create project info
# Create project-info.json (see above)

# 5. Capture screenshots
# Take 5-6 key screenshots of UI
```

### Day 2: Generate Video (30-60 minutes)

```bash
# 1. Open Claude.dev or Cursor
# 2. Install bragif skill
# 3. Run:

/brag Create a professional 60-second launch video 
for the Distributed Order Management System. Include:
- Product checkout flow
- Real-time order tracking
- Jaeger distributed traces
- Key achievement: automatic compensation
- Call-to-action: http://localhost:3000
```

### Day 3: Refine & Export (20-30 minutes)

```bash
# 1. Review video in preview (http://localhost:3000)
# 2. Request adjustments if needed:

/brag Adjust video: 
- Increase emphasis on compensation
- Add more Jaeger trace visualization
- Make music slightly softer
- Add statistics overlay

# 3. Export final video
npx hyperframes export \
  --input src/video.tsx \
  --output dist/distributed-order-management-launch.mp4 \
  --quality high

# 4. Verify video plays correctly
# Open dist/distributed-order-management-launch.mp4
```

---

## Using the Generated Video

### 1. YouTube Upload

```
Title: "Distributed Order Management System - Product Launch"

Description:
A distributed order management system demonstrating 
saga patterns, automatic compensation, and end-to-end 
distributed tracing with OpenTelemetry and Jaeger.

When payment fails, inventory is automatically released 
without manual intervention. Here's how it works:

✨ Key Features:
- 8 microservices (Order, Inventory, Payment, Saga, Notification, Gateway, Observability)
- Automatic compensation on payment failure
- Full distributed tracing with W3C Trace Context
- Real-time order tracking with React UI
- 161 comprehensive tests

🚀 Get Started:
- GitHub: https://github.com/[user]/[repo]
- Live Demo: http://localhost:3000
- Documentation: [link to docs]

Tags: #DistributedSystems #Microservices #SagaPattern 
      #OpenSource #OrderManagement #Distributed Transactions
```

### 2. LinkedIn Post

```
🚀 Just launched: Distributed Order Management System

The problem we solved:
When payment fails in a distributed system, what happens 
to reserved inventory? Typically: manual database fixes, 
angry customers, sleepless nights.

Our solution:
Automatic compensation. Payment fails → Inventory released 
automatically → Customer notified → Zero manual intervention.

Built with:
✅ 8 microservices in perfect sync
✅ Saga pattern for distributed transactions
✅ End-to-end distributed tracing (OpenTelemetry + Jaeger)
✅ Real-time order tracking
✅ 161 tests proving correctness

Watch it in action: [video link]

Open source & ready to learn from. 

#DistributedSystems #Microservices #OpenSource #ProductLaunch
```

### 3. Twitter/X Post

```
Just shipped: Distributed Order Management System 🚀

Payment fails → Compensation auto-triggers → Inventory released

No manual intervention. No data loss. No sleepless nights.

Saga pattern + distributed tracing done right ✅

Watch the full demo 👇
[video link]

#DistributedSystems #Microservices #SagaPattern #OpenSource
```

### 4. Email Newsletter

```
Subject: Automatic Compensation for Distributed Systems 
        - New Open Source Project

Hi [Subscriber],

We just launched something we're really proud of: 
a Distributed Order Management System that demonstrates 
how to handle failures gracefully in microservices.

The key innovation: When payment fails, inventory is 
automatically released without any manual intervention. 
The entire flow is observable end-to-end via Jaeger.

See the 60-second demo → [video link]

What's included:
• 8 fully functional microservices
• 161 comprehensive tests
• Full distributed tracing
• Production-ready React UI

Documentation: [link]
GitHub: [link]
Try it locally: [setup instructions]

Questions? Reply to this email.

Best,
[Team Name]
```

---

## Troubleshooting

### Video Preview Stuck
```bash
# Kill preview process
pkill -f "hyperframes preview"

# Restart
npx hyperframes preview
```

### Bragif Skill Not Found
```bash
# Manually reinstall skill in Claude.dev
1. Click Settings
2. Search "bragif"
3. Click "Install"
4. Reload IDE
```

### Screenshots Not Embedded
```bash
# Verify paths in project-info.json
# Paths should be relative: "./screenshots/checkout.png"
# Not absolute paths

# Rebuild
npx hyperframes build --force
```

### Music License Issues
```bash
# Use royalty-free music option
npx hyperframes build --music "royalty-free-upbeat"

# Or specify music URL
npx hyperframes build --music-url "https://example.com/music.mp3"
```

---

## Advanced Customization

### Custom Themes

```bash
# Create custom theme
cat > theme.json << 'EOF'
{
  "name": "tech-startup",
  "colors": {
    "primary": "#0066cc",
    "secondary": "#00aa44",
    "accent": "#ff6b35",
    "background": "#1a1a1a",
    "text": "#ffffff"
  },
  "fonts": {
    "title": "Inter Bold",
    "body": "Inter Regular",
    "mono": "JetBrains Mono"
  },
  "animations": "smooth-transition",
  "musicGenre": "electronic"
}
EOF

# Apply theme
npx hyperframes build --theme theme.json
```

### Multi-Language Support

```bash
/brag Create launch videos in:
- English
- Spanish
- French
- German
- Japanese

For the Distributed Order Management System
```

### Variations for Different Platforms

```bash
# YouTube (16:9, 60s)
/brag Create a 60-second 1920x1080 launch video for 
Distributed Order Management System

# TikTok (9:16, 30s)
/brag Create a 30-second 1080x1920 vertical video for 
Distributed Order Management System

# LinkedIn (1:1, 45s)
/brag Create a 45-second 1080x1080 square video for 
Distributed Order Management System

# Instagram Reel (9:16, 15s)
/brag Create a 15-second 1080x1920 vertical Reels video for 
Distributed Order Management System
```

---

## Next Steps After Video

### 1. Create Product Hunt Post
Use the generated video as hero video

### 2. Announce on Twitter/X
Share video with hashtags #OpenSource #ProductLaunch

### 3. Write Blog Post
Include embedded video + technical details

### 4. Update GitHub README
Add video showcase with link

### 5. Email Newsletter
Send to subscriber list

### 6. LinkedIn Announcement
Tag relevant communities and influencers

---

## Success Metrics

Track video performance:

```
YouTube:
- Views: Target 1k+ in first week
- Click-through rate: >5%
- Engagement: Comments, shares

LinkedIn:
- Impressions: Target 5k+
- Likes: >50
- Comments: >10
- Shares: >5

GitHub:
- Stars: Increases post-launch
- Clones: Increases post-launch
- Issues: Quality engagement

Traffic:
- Unique visitors: Track with analytics
- Demo clicks: http://localhost:3000
- Conversion: GitHub fork/star
```

---

## Summary

Using Bragif + HyperFrames, you can generate a professional product launch video for the Distributed Order Management System in 2-3 hours total:

1. **Setup** (30 min): Install tools, capture screenshots
2. **Generate** (30 min): Run /brag command, refine
3. **Export** (30 min): Polish and export

**Result**: Professional 60-second MP4 video with music, motion graphics, and share copy - ready to launch on YouTube, LinkedIn, Twitter, and more.

The video showcases:
- ✅ Product functionality (checkout, tracking)
- ✅ Key achievement (automatic compensation)
- ✅ Technical prowess (distributed tracing)
- ✅ Production quality (music, animation)

Perfect for Phase 11 final announcement!

---

*Generate your product launch video with one command using Bragif + HyperFrames*

