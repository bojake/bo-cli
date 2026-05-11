export default class LegacyCoordinator {
  constructor(deps: unknown) {}

  execute(task: string) {
    return task;
  }

  retry = async () => true;
}

const fallbackMode = "safe";

export { fallbackMode };
