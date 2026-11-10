// A viewer as the 抽奖页 names one: by nickname, else by UID (a pool's called viewer, or one who never sent a danmaku in it),
// else as `fallback`. The 桌宠页 names them the same way.
export const viewerName = (viewer, fallback = '') => viewer.nickname || (viewer.uid ? `UID ${viewer.uid}` : fallback);
