# Rule: Avalonia ViewModels & Wizard Standard

When creating, refactoring, or extending ViewModels, multi-step wizards, or UI navigation in this repository:

1. **Mandatory Skill**: Always consult and follow the instructions in the `avalonia-viewmodels-zafiro` skill ([.agents/skills/ag-avalonia-viewmodels-zafiro/SKILL.md](file:///d:/Projects/Vps%20Manager/.agents/skills/ag-avalonia-viewmodels-zafiro/SKILL.md)).
2. **Key Patterns**:
   - Apply functional-reactive patterns (ReactiveUI / Zafiro) for state handling and observation.
   - Utilize enhanced command patterns (`IEnhancedCommand`) with progress tracking and descriptive metadata where appropriate.
   - For multi-step procedures (deployment configuration, initial VPS setup, complex database backups/restores), follow the `SlimWizard` / `WizardBuilder` declarative flow patterns.
   - Maintain clean separation between Views and ViewModels using proper composition and view locators.
