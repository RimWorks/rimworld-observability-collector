// lets a warning chip open the gear popover it cannot reach by props.
// openTreeTab and pauseLive are bump counters: +1 asks the call-tree panel to show its
// tree tab, and asks the live view to pin the frame on screen.
export const uiSignals = $state({ settingsOpen: false, openTreeTab: 0, pauseLive: 0 });
