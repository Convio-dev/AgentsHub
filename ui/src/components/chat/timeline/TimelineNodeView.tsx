import React from 'react';
import { TimelineNode } from '../../../types/agent';
import { TimelineCommandNode } from './TimelineCommandNode';
import { TimelineFileNode } from './TimelineFileNode';
import { TimelineDiffNode } from './TimelineDiffNode';
import { TimelineThinkingNode } from './TimelineThinkingNode';

/**
 * Single entry point for rendering one timeline node, so the top level and the inside of a group
 * share exactly one dispatch table.
 */
export const TimelineNodeView: React.FC<{ node: TimelineNode }> = ({ node }) => {
  switch (node.type) {
    case 'command':
      return <TimelineCommandNode node={node} />;
    case 'file':
      return <TimelineFileNode node={node} />;
    case 'diff':
      return <TimelineDiffNode node={node} />;
    case 'thought':
      return <TimelineThinkingNode node={node} />;
    default:
      return null;
  }
};
