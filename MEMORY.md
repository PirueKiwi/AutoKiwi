# AutoKiwi Project Memory Map

## 🎯 Core Vision
**Two-Part Autonomous System:**
- **AutoKiwi.exe** → generates application + learning_data.json
- **AutoKiwiPilot.exe** → reads learning_data.json + controls via user32.dll

**Ultimate Goal:** AI creates software that AI can then operate autonomously.

---

## 🚀 CURRENT STATUS (Latest Update)

### ✅ COMPLETED TODAY
- **Chrome Extension + Native Host** - WORKING! 🎉
  - Git integration funguje perfektně
  - Visual feedback v chatu
  - Automatic memory management
  - Commands: !git status, !commit memory, !pull memory

### 🔧 IN PROGRESS
- **Architecture redesign** - from complex to simple
- **DataCollector module** - UI metadata extraction
- **Learning data structure** - finalized design

### 🎯 NEXT PRIORITIES
1. **DataCollector implementation** - bridge between old/new architecture
2. **WindowController** - user32.dll wrapper
3. **AutoKiwi.exe redesign** - simplified main window

---

## 🏗️ Architecture Status

### ✅ What Works
- **Event Bus System** - message passing
- **LM Studio Integration** - local LLM communication  
- **Roslyn Compilation** - C# code compilation
- **Form Analyzer** - Windows Forms UI analysis
- **Monitoring Dashboard** - workflow visualization
- **Adaptive Learning** - model selection based on success
- **Git Memory System** - Chrome extension + native host ⭐ NEW!

### ❌ Needs Redesign  
- **Over-engineered architecture** - too complex for chat limits
- **Missing Pilot component** - no user32.dll automation yet
- **No learning data export** - UI analysis stays internal

---

## 🔧 Target Architecture (Simplified)

```
AutoKiwi.exe (Main Generator)
├── Generator.cs           // Single LLM communicator
├── DataCollector.cs       // UI metadata extraction ⭐ NEXT
├── Compiler.cs           // Roslyn wrapper
└── MainWindow.cs         // Simple control UI

AutoKiwiPilot.exe (Autonomous Controller) 
├── WindowController.cs    // user32.dll wrapper
├── DataReader.cs         // learning_data.json parser
├── ActionExecutor.cs     // workflow automation
└── ResolutionManager.cs  // DPI/scaling handling

Chrome Extension + Native Host
├── Command detection in chat ✅
├── Automatic Git operations ✅
├── Memory persistence ✅
└── Visual feedback ✅
```

---

## 📊 Learning Data Structure

**Core Components:**
- **AppMetadata** - basic app info, success rates, repair attempts
- **UIElement[]** - every control with positions, types, actions
- **WorkflowSequence[]** - learned automation patterns
- **DisplayInfo** - resolution, DPI, window borders
- **DevelopmentNotes** - errors, fixes, recommendations

**Key Features:**
- Resolution independence (relative + absolute positions)
- Window border compensation (title bar, borders)
- Action success tracking
- Development intelligence preservation

---

## 🔄 Chat Continuation Protocol

### When Starting New Chat:
1. **`!pull memory`** - get latest MEMORY.md
2. **Share current status** - what's working, what's next
3. **Identify immediate task** - 1-2 components max
4. **Update memory** - `!commit memory` when done

### Development Strategy:
- **1 chat = 1 module** (DataCollector, WindowController, etc.)
- **Code in artifacts** - survives chat limits
- **Test immediately** - verify before moving on
- **Memory updates** - after each major milestone

---

## 🎯 Development Strategy

### Phase 1: Core Redesign (IN PROGRESS)
1. **Simplify AutoKiwi.exe** - single window, minimal abstractions
2. **Implement DataCollector** - extract UI data during development ⭐ CURRENT
3. **Create learning_data.json** - export format

### Phase 2: Pilot Development  
1. **WindowController** - user32.dll automation
2. **Resolution handling** - scale coordinates properly
3. **Workflow execution** - run learned sequences

### Phase 3: Integration
1. **Round-trip testing** - AutoKiwi → Pilot → validation
2. **Learning loop** - Pilot feedback improves generation
3. **Polish & optimization**

---

## 🎯 Today's Battle Plan

### ✅ COMPLETED
- [x] Git integration working perfectly
- [x] Chrome extension functional
- [x] Native host communication
- [x] Memory persistence system

### 🔄 CURRENT FOCUS
- [ ] DataCollector module (UI metadata extraction) ⭐ NEXT
- [ ] AutoKiwi.exe main window redesign
- [ ] WindowController (user32.dll core)
- [ ] Integration testing

---

## 🧠 Key Decisions Made

### Git Integration Approach
- **Chrome Extension** - detects commands in chat
- **Native Host** - GitHost.exe handles actual Git operations
- **Windows Credentials** - uses existing Git authentication
- **File-based memory** - MEMORY.md as persistent state

### Architecture Philosophy
- **Simplicity over complexity** - chat limits force discipline
- **Modular development** - one component per session
- **Immediate testing** - verify functionality before moving on
- **External memory** - Git as backup brain

---

## 🚨 Known Issues & Solutions

1. **Chat Context Loss** → Git-based memory system ✅ SOLVED
2. **Over-engineered architecture** → Simplification in progress
3. **Resolution scaling** → WindowController will handle
4. **user32.dll reliability** → Careful implementation needed
5. **Learning Data Volume** - balance between detail and file size
6. **Window Border Handling** - title bar and frame affect positioning

---

## 💡 Key Insights from Development

- **LLM works best with single-purpose modules** (not orchestrated systems)
- **Direct user32.dll calls more reliable** than automation frameworks
- **Learning data is the secret sauce** - quality of metadata determines success
- **Resolution independence is critical** - users have different screens
- **Chat limits force architectural discipline** - simpler is better
- **Git integration enables autonomous memory management** ⭐ NEW INSIGHT

---

## 🎯 Next Session Checklist

When continuing:
- [ ] `!pull memory` to get latest state
- [ ] Confirm current component focus
- [ ] Set realistic scope (1-2 components max)
- [ ] Update memory with progress
- [ ] `!commit memory` when done

**Current Status:** Git integration complete, DataCollector is next priority.

---

## 🔥 Git Commands Available

- `!git status` - repository status
- `!pull memory` - get latest MEMORY.md
- `!commit memory` - save current memory state
- `!sync memory` - pull + update + commit

**Extension working perfectly!** 🚀

---

## 📈 Success Metrics

### Technical Goals
- [ ] Single .exe generates working app + learning data
- [ ] Pilot can control generated app autonomously  
- [ ] Resolution independence works across different screens
- [ ] Learning improves generation quality over time

### Practical Goals
- [ ] Faster development cycle (< 30 seconds app generation)
- [ ] Reliable automation (> 90% action success rate)
- [ ] Chat-limit resilient architecture
- [ ] Real user32.dll behavior (handles overlapping windows, etc.)