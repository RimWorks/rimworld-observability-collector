#!/usr/bin/env node
import { bumpWorkshop } from '@rimworks/mod-ci';

const stagePath = await bumpWorkshop({
  workshopId: '3733585062',
  solution: 'RimObs.sln',
});

console.log(`pushed from ${stagePath}`);
