# Video Generation Prompt for Brag/HyperFrames

## Command to Run in Claude.dev or Cursor IDE

Use the `/brag` command with this exact prompt:

---

Create a 60-second product launch video for the Distributed Order Management System with these elements:

**OPENING (5s): Title animation**
- Text: "Distributed Order Management System"
- Subtitle: "Automatic Compensation When Payment Fails"
- Background: Blue gradient (#0066cc)
- Animation: Fade in

**CHECKOUT FLOW (15s): Show product browsing and cart**
- Scene 1: Product catalog with items
- Scene 2: Add items to cart (animation)
- Scene 3: Cart total updating in real-time

**ORDER CONFIRMATION (10s): Order placement**
- Show: Checkout button click
- Display: Order confirmation
- Highlight: Order ID

**REAL-TIME TRACKING (10s): Order status updates**
- Status transitions: Pending → Confirmed
- Animation: Checkmarks and progress

**INNOVATION HIGHLIGHT (10s): Payment failure scenario**
- Show: Payment fails (red X)
- Answer: "Automatic Compensation Triggered"

**COMPENSATION IN ACTION (8s): Inventory release**
- Show: Inventory being released
- Highlight: "No Manual Intervention Required"
- Color: Green success animation

**DISTRIBUTED TRACING (10s): Jaeger visualization**
- Show: Complex trace diagram
- Emphasize: "Full end-to-end visibility"

**KEY METRICS (7s): Animation of statistics**
- "8 Microservices"
- "161 Tests"
- "20,000 Lines of Code"

**CALL-TO-ACTION (5s): Closing**
- Text: "Ready to build distributed systems right?"
- Button: "Try it now: http://localhost:3000"

**PRODUCTION SPECS:**
- Resolution: 1920x1080
- Frame Rate: 30fps
- Duration: Exactly 60 seconds
- Format: MP4
- Music: Upbeat, professional electronic
- Colors: Primary #0066cc, Success #00aa44, Text white

**ALSO GENERATE:**
- YouTube description (200+ words, SEO optimized)
- LinkedIn post (150 words with hashtags)
- Twitter/X post (280 chars max)
- GitHub README snippet

---

## What This Prompt Does

1. **Generates HTML/CSS/JavaScript** for a professional animated video
2. **Renders to MP4** (1920x1080, 30fps, 60 seconds)
3. **Exports Social Media Copy** for 4 platforms:
   - YouTube (SEO-optimized description)
   - LinkedIn (professional network post)
   - Twitter/X (280 character limit)
   - GitHub (README snippet)

## Expected Output Location

After generation, files appear in:
```
dist/
├── product-launch-video.mp4          ← Main video file
├── product-launch-video-preview.gif  ← Preview animation
└── social-media-copy.md              ← Marketing copy
```

## Next Steps After Generation

1. Copy MP4 to project root as `PRODUCT_LAUNCH_VIDEO.mp4`
2. Use social media copy for Track C (sharing)
3. Upload to YouTube, LinkedIn, Twitter, GitHub

---

## Repository Reference

This uses the **brag** repository: https://github.com/latent-spaces/brag

Powered by HyperFrames for professional video generation.
