#!/usr/bin/env node
import { bumpWorkshop } from '@rimworks/mod-ci';

const stagePath = await bumpWorkshop({
  workshopId: process.env.WORKSHOP_ID || '3733585062',
  solution: 'RimObs.slnx',
});

console.log(`pushed from ${stagePath}`);
